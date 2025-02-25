// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.ObjectPool;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using InstructionPointer = System.Int32;

namespace Wacs.Core.Runtime
{
    public struct ExecStat
    {
        public long duration;
        public long count;
    }

    public class ExecContext
    {
        // public int _sequenceCount;

        public const int AbortSequence = -2;
        public static readonly Frame NullFrame = new();

        private static readonly ValType[] EmptyLocals = Array.Empty<ValType>();
        private readonly Stack<Frame> _callStack;
        private readonly ObjectPool<Frame> _framePool;

        private readonly Stack<BlockTarget> _linkLabelStack = new();
        private readonly ArrayPool<Value> _localsDataPool;
        public readonly RuntimeAttributes Attributes;
        public readonly Stopwatch InstructionTimer = new();

        public readonly InstructionSequence linkedInstructions = new();
        public readonly OpStack OpStack;

        public readonly Stopwatch ProcessTimer = new();

        public readonly Store Store;

        public InstructionBase[] _currentSequence;
        // public List<InstructionBase> _sequenceInstructions;


        public Frame Frame = NullFrame;
        public int InstructionPointer;
        public int LinkOpStackHeight;
        public bool LinkUnreachable;
        public int LinkLocalCount;
        public readonly Dictionary<Value, int> LinkConstants = new();

        public Dictionary<ushort, ExecStat> Stats = new();
        public long steps;

        public ExecContext(Store store, RuntimeAttributes? attributes = default)
        {
            Store = store;
            Attributes = attributes ?? new RuntimeAttributes();

            _framePool = new DefaultObjectPool<Frame>(new StackPoolPolicy<Frame>(), Attributes.MaxCallStack)
                .Prime(Attributes.InitialCallStack);
            
            _localsDataPool = ArrayPool<Value>.Create(Attributes.MaxFunctionLocals, Attributes.LocalPoolSize);
            
            _callStack = new (Attributes.InitialCallStack);
            
            // _hostReturnSequence = InstructionSequence.Empty;
            
            // _currentSequence = _hostReturnSequence;
            
            // _sequenceCount = _currentSequence.Count;
            // _sequenceInstructions = _currentSequence._instructions;
            InstructionPointer = -1;

            OpStack = new(Attributes.MaxOpStack);
        }

        public int StackHeight => _callStack.Count;

        public InstructionBaseFactory InstructionFactory => Attributes.InstructionFactory;

        public MemoryInstance DefaultMemory => Store[Frame.Module.MemAddrs[default]];

        public int LabelHeight => _linkLabelStack.Count;

        public void CacheInstructions()
        {
            _currentSequence = linkedInstructions._instructions.ToArray();
        }

        public void PrintInstruction(int i)
        {
            var inst = linkedInstructions[i];
            if (inst is BlockTarget label)
            {
                Console.Error.WriteLine($"[0x{i:x8}] {label.Label.StackHeight} ({inst.StackDiff:+####;-####;0}) {inst}");
            }
            else if (inst is InstEnd)
            {
                Console.Error.WriteLine($"[0x{i:x8}]  {inst.StackDiff:+####;-####;0} {inst}");
            }
            else
            {
                Console.Error.WriteLine($"[0x{i:x8}]     {inst.StackDiff:+####;-####;0} {inst}");
            }
        }

        public InstructionPointer GetPointer()
        {
            return InstructionPointer;
        }

        [Conditional("STRICT_EXECUTION")]
        public void Assert([NotNull] object? objIsNotNull, string message)
        {
            if (objIsNotNull == null)
                throw new TrapException(message);
        }

        [Conditional("STRICT_EXECUTION")]
        public void Assert(bool assertion, string message)
        {
            if (!assertion)
                throw new TrapException(message);
        }

        public ValType StackTopTopType() => 
            OpStack.Peek().Type.TopHeapType(Frame.Module.Types);

        public Frame ReserveFrame(
            ModuleInstance module,
            FunctionType type,
            FuncIdx index,
            ValType[]? locals = default)
        {
            locals ??= EmptyLocals;
            
            var frame = _framePool.Get();
            frame.Module = module;
            frame.Type = type;
            frame.Index = index;
            frame.ContinuationAddress = InstructionPointer;
            frame.ReturnLabel.Arity = type.ResultType.Arity;
            // int capacity = type.ParameterTypes.Types.Length + locals.Length;
            // var localData = _localsDataPool.Rent(capacity);
            // frame.Locals = new(localData, type.ParameterTypes.Types, locals, true);
            frame.StackHeight = OpStack.Count;
            
            return frame;
        }

        public void PushFrame(Frame frame)
        {
            if (_callStack.Count >= Attributes.MaxCallStack)
                throw new WasmRuntimeException($"Runtime call stack exhausted {_callStack.Count}");

            Frame = frame;
            _callStack.Push(frame);
        }

        public InstructionPointer PopFrame()
        {
            var frame = _callStack.Pop();
            
            Assert( OpStack.Count >= frame.Arity + frame.StackHeight,
                $"Instruction return failed. Operand stack underflow");

            int localsCount = frame.Locals.Length;
            // int resultCount = frame.Type.ResultType.Arity;
            int resultCount = frame.ReturnLabel.Arity;
            int resultsHeight = frame.StackHeight + resultCount - localsCount;
            OpStack.ShiftResults(resultCount, resultsHeight);
            frame.Locals = null;
            Frame = _callStack.Count > 0 ? _callStack.Peek() : NullFrame;
            
            var address = frame.ContinuationAddress;
            _framePool.Return(frame);
            return address;
        }

        public void ClearCallStack()
        {
            for (int i = _callStack.Count; i > 0; --i)
            {
                var frame = _callStack.Pop();
                frame.Locals = null;
                _framePool.Return(frame);
            }
        }

        public Frame ReuseFrame()
        {
            return _callStack.Peek();
        }

        public void TailCall(FuncAddr addr)
        {
            Assert( Store.Contains(addr),
                $"Failure in Function Invocation. Address does not exist {addr}");
            var funcInst = Store[addr];
            
            switch (funcInst)
            {
                case FunctionInstance wasmFunc:
                    wasmFunc.TailInvoke(this);
                    return;
                // case HostFunction hostFunc:
                // {
                //     if (funcInst.IsAsync)
                //         throw new WasmRuntimeException("Cannot call asynchronous function synchronously");
                //     
                //     hostFunc.TailInvoke(this);
                //     return;
                // }
            }
            throw new WasmRuntimeException($"Unexpected function {funcInst} at address {addr}");
        }

        public BlockTarget? FindLabel(int depth)
        {
            var instructions = _currentSequence;
            int ptr = InstructionPointer;
            BlockTarget? label = null;
            
            while (ptr > Frame.Head && label == null)
            {
                var inst = instructions[--ptr];
                switch (inst)
                {
                    case BlockTarget target: 
                        label = target;
                        break;
                    case InstEnd:
                        depth += 1;
                        break;
                }
            }

            if (label is null && ptr == Frame.Head)
                return null;
            
            for (int i = 0; i < depth && label != null; i++)
            {
                label = label?.EnclosingBlock ?? null;
            }

            return label;
        }

        public void ResetStack(Label label)
        {
            OpStack.PopTo(label.StackHeight + Frame.StackHeight);
        }

        public void FlushCallStack()
        {
            ClearCallStack();
            OpStack.Clear();

            Frame = NullFrame;
            InstructionPointer = -1;
        }

        public void ClearLinkLabels() => _linkLabelStack.Clear();

        public void PushLabel(BlockTarget inst) => _linkLabelStack.Push(inst);

        public BlockTarget PopLabel() => _linkLabelStack.Pop();

        public BlockTarget PeekLabel() => _linkLabelStack.Peek();


        // @Spec 4.4.10.1 Function Invocation
        public async Task InvokeAsync(FuncAddr addr)
        {
            //1.
            Assert( Store.Contains(addr),
                $"Failure in Function Invocation. Address does not exist {addr}");
            
            //2.
            var funcInst = Store[addr];
            switch (funcInst)
            {
                case FunctionInstance wasmFunc:
                    wasmFunc.Invoke(this);
                    return;
                case HostFunction hostFunc:
                {
                    if (hostFunc.IsAsync)
                        await hostFunc.InvokeAsync(this);
                    else
                        hostFunc.Invoke(this);
                } return;
            }
        }

        public void Invoke(FuncAddr addr)
        {
            //1.
            Assert( Store.Contains(addr),
                $"Failure in Function Invocation. Address does not exist {addr}");
            
            //2.
            var funcInst = Store[addr];
            
            switch (funcInst)
            {
                case FunctionInstance wasmFunc:
                    wasmFunc.Invoke(this);
                    return;
                case HostFunction hostFunc:
                {
                    if (funcInst.IsAsync)
                        throw new WasmRuntimeException("Cannot call asynchronous function synchronously");
                    
                    hostFunc.Invoke(this);
                } return;
            }
        }

        // @Spec 4.4.10.2. Returning from a function
        public void FunctionReturn()
        {
            Assert( OpStack.Count >= Frame.Arity,
                $"Function Return failed. Stack did not contain return values");
            var address = PopFrame();
            InstructionPointer = address;
        }

        public List<(string, int)> ComputePointerPath()
        {
            Stack<(string, int)> ascent = new();
            int idx = InstructionPointer;
            
            // foreach (var label in Frame.EnumerateLabels().Select(target => target.Label))
            // {
            //     var pointer = (label.Instruction.GetMnemonic(), idx);
            //     ascent.Push(pointer);
            //
            //     idx = label.ContinuationAddress;
            //     
            //     switch ((OpCode)label.Instruction)
            //     {
            //         case OpCode.If: ascent.Push(("InstIf", 0));
            //             break;
            //         case OpCode.Else: ascent.Push(("InstElse", 1));
            //             break;
            //         case OpCode.Block: ascent.Push(("InstBlock", 0));
            //             break;
            //         case OpCode.Loop: ascent.Push(("InstLoop", 0));
            //             break;
            //     }
            //     
            // }
            //
            // ascent.Push(("Function", (int)Frame.Index.Value));

            return ascent.Select(a => a).ToList();
        }

        public void ResetStats()
        {
            Stats.Clear();
            foreach (OpCode opcode in Enum.GetValues(typeof(OpCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
            foreach (GcCode opcode in Enum.GetValues(typeof(GcCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
            foreach (ExtCode opcode in Enum.GetValues(typeof(ExtCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
            foreach (SimdCode opcode in Enum.GetValues(typeof(SimdCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
            foreach (AtomCode opcode in Enum.GetValues(typeof(AtomCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
            foreach (WacsCode opcode in Enum.GetValues(typeof(WacsCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
        }

        public OpCode GetEndFor() => 
            FindLabel(0) is { } label ? label.Op : OpCode.Func;

        public ModuleInstance? GetModule(FuncAddr funcAddr)
        {
            var functionInstance = Store[funcAddr];
            switch (functionInstance)
            {
                case FunctionInstance wasmFunc: return wasmFunc.Module;
                case HostFunction hostFunc:
                //TODO: maybe implement ref binding for host functions
                default:
                    return null;
            }
        }

        public void LinkFunction(FunctionInstance instance)
        {
            InstructionPointer offset = linkedInstructions.Count;
            instance.LinkedOffset = offset;

            LinkOpStackHeight = 0;
            LinkUnreachable = false;
            ClearLinkLabels();
            PushLabel(new InstExpressionProxy(new Label
            {
                Instruction = OpCode.Func,
                StackHeight = 0,
                Arity = instance.Type.ResultType.Arity
            }));
            LinkLocalCount = instance.Type.ParameterTypes.Arity + instance.Locals.Length;
            linkedInstructions.Append(instance.Body.Flatten().Select((inst,idx)=>inst.Link(this, offset+idx)));
            
            instance.Length = linkedInstructions.Count - instance.LinkedOffset;
        }
    }
}