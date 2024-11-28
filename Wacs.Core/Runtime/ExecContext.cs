// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.ObjectPool;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime
{
    public struct ExecStat
    {
        public long duration;
        public long count;
    }

    public class ExecContext
    {
        private static readonly Frame NullFrame = new();

        private static readonly ValType[] EmptyLocals = Array.Empty<ValType>();
        private readonly Stack<Frame> _callStack;
        private readonly ObjectPool<Frame> _framePool;
        private readonly InstructionSequence _hostReturnSequence;
        
        private readonly ArrayPool<Value> _localsDataPool;
        public readonly RuntimeAttributes Attributes;
        public readonly OpStack OpStack;

        public readonly Store Store;

        private Stack<Value> _asideVals = new();

        private InstructionSequence _currentSequence;
        private InstructionBase[] _sequenceInstructions;
        private int _sequenceCount;
        private int _sequenceIndex;
        public InstructionPointer GetPointer() => new(_currentSequence, _sequenceIndex);

        public Frame Frame = NullFrame;

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
            
            _hostReturnSequence = InstructionSequence.Empty;
            
            _currentSequence = _hostReturnSequence;
            _sequenceCount = _currentSequence.Count;
            _sequenceInstructions = _currentSequence._instructions;
            _sequenceIndex = -1;

            OpStack = new(Attributes.MaxOpStack);
        }

        public readonly Stopwatch ProcessTimer = new();
        public readonly Stopwatch InstructionTimer = new();

        public IInstructionFactory InstructionFactory => Attributes.InstructionFactory;

        public MemoryInstance DefaultMemory => Store[Frame.Module.MemAddrs[default]];

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
            // frame.Labels = _labelStack.GetSubStack();
            frame.Index = index;
            frame.ContinuationAddress = new InstructionPointer(_currentSequence, _sequenceIndex);
            
            frame.ClearLabels();
            int capacity = type.ParameterTypes.Types.Length + locals.Length;
            var localData = _localsDataPool.Rent(capacity);
            frame.Locals = new(localData, type.ParameterTypes.Types, locals);
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
            Frame = _callStack.Count > 0 ? _callStack.Peek() : NullFrame;
            
            // frame.Labels.Drop();
            if (frame.Locals.Data != null)
                frame.ReturnLocals(_localsDataPool);

            var address = frame.ContinuationAddress;
            _framePool.Return(frame);
            return address;
        }

        public void ResetStack(Label label)
        {
            for (int c = OpStack.Count, h = label.StackHeight + Frame.StackHeight; c > h; --c)
            {
                OpStack.PopAny();
            }
        }

        public void FlushCallStack()
        {
            for (int i = _callStack.Count; i > 0; --i)
                PopFrame();
            
            OpStack.Clear();

            Frame = NullFrame;
            _currentSequence = _hostReturnSequence;
            _sequenceCount = _currentSequence.Count;
            _sequenceInstructions = _currentSequence._instructions;
            _sequenceIndex = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnterSequence(InstructionSequence seq)
        {
            _currentSequence = seq;
            _sequenceCount = _currentSequence.Count;
            _sequenceInstructions = _currentSequence._instructions;
            _sequenceIndex = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResumeSequence(InstructionPointer pointer)
        {
            _currentSequence = pointer.Sequence;
            _sequenceCount = _currentSequence.Count;
            _sequenceInstructions = _currentSequence._instructions;
            _sequenceIndex = pointer.Index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FastForwardSequence()
        {
            //Go to penultimate instruction since we pre-increment on pointer advance.
            _sequenceIndex = _sequenceCount - 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RewindSequence()
        {
            //Go back to the first instruction in the sequence
            _sequenceIndex = -1;
        }

        // @Spec 4.4.9.1. Enter Block
        public void EnterBlock(BlockTarget target, Block block)
        {
            //HACK: Labels are a linked list with each node residing on its respective block instruction.
            Frame.PushLabel(target);
            
            //Sets the Pointer to the start of the block sequence
            EnterSequence(block.Instructions);
        }

        // @Spec 4.4.9.2. Exit Block
        public void ExitBlock()
        {
            var addr = Frame.PopLabels(0);
            // We manage separate stacks, so we don't need to relocate the operands
            // var vals = OpStack.PopResults(label.Type);
            ResumeSequence(addr);
        }

        // @Spec 4.4.10.1 Function Invocation
        public async Task Invoke(FuncAddr addr)
        {
            //1.
            Assert( Store.Contains(addr),
                $"Failure in Function Invocation. Address does not exist {addr}");
            
            //2.
            var funcInst = Store[addr];
            switch (funcInst)
            {
                case FunctionInstance wasmFunc:
                    Invoke(wasmFunc, wasmFunc.Index);
                    return;
                case HostFunction hostFunc:
                    if (hostFunc.IsAsync)
                        await InvokeAsync(hostFunc);
                    else
                        Invoke(hostFunc);
                    return;
            }
        }

        private void Invoke(FunctionInstance wasmFunc, FuncIdx idx)
        {
            //3.
            var funcType = wasmFunc.Type;
            //4.
            var t = wasmFunc.Locals;
            //5. *Instructions will be handled in EnterSequence below
            //var seq = wasmFunc.Definition.Body;
            //6.
            Assert( OpStack.Count >= funcType.ParameterTypes.Arity,
                $"Function invocation failed. Operand Stack underflow.");
            //7.
            Assert(_asideVals.Count == 0,
                $"Shared temporary stack had values left in it.");
            OpStack.PopResults(funcType.ParameterTypes, ref _asideVals);
            //8.
            //Push the frame and operate on the frame on the stack.
            var frame = ReserveFrame(wasmFunc.Module, funcType, idx, t);
            // frame.FuncId = wasmFunc.Id;
                
            int li = 0;
            int localCount = funcType.ParameterTypes.Arity + t.Length;
            //Load parameters
            while (_asideVals.Count > 0)
            {
                // frame.Locals.Set((LocalIdx)li, _asideVals.Pop());
                frame.Locals.Data[li] = _asideVals.Pop();
                li += 1;
            }
            //Set the Locals to default
            for (int ti = 0; li < localCount; ++li, ++ti)
            {
                // frame.Locals.Set((LocalIdx)li, new Value(t[ti]));
                frame.Locals.Data[li] = new Value(t[ti]);
            }

            //9.
            PushFrame(frame);
            
            //10.
            frame.PushLabel(wasmFunc.Body.LabelTarget); 
            
            frame.ReturnLabel.Arity = funcType.ResultType.Arity;
            frame.ReturnLabel.Instruction = OpCode.Func;
            frame.ReturnLabel.ContinuationAddress = new InstructionPointer(_currentSequence, _sequenceIndex);
            frame.ReturnLabel.StackHeight = 0;
            
            
            EnterSequence(wasmFunc.Body.Instructions);
        }

        private void Invoke(HostFunction hostFunc)
        {
            var funcType = hostFunc.Type;
            
            //Fetch the parameters
            OpStack.PopScalars(funcType.ParameterTypes, hostFunc.ParameterBuffer, hostFunc.PassExecContext?1:0);

            if (hostFunc.PassExecContext)
            {
                hostFunc.ParameterBuffer[0] = this;
            }
            
            //Pass them
            hostFunc.Invoke(hostFunc.ParameterBuffer, OpStack);
        }
        
        // @Spec 4.4.10.2. Returning from a function
        public void FunctionReturn()
        {
            //3.
            Assert( OpStack.Count >= Frame.Arity,
                $"Function Return failed. Stack did not contain return values");
            //4. Since we have a split stack, we can leave the results in place.
            // var vals = OpStack.PopResults(Frame.Type.ResultType);
            //5.
            //6.
            var address = PopFrame();
            //7. split stack, values left in place 
            //8.
            ResumeSequence(address);
        }
        
        private async ValueTask InvokeAsync(HostFunction hostFunc)
        {
            var funcType = hostFunc.Type;
            
            //Fetch the parameters
            OpStack.PopScalars(funcType.ParameterTypes, hostFunc.ParameterBuffer, hostFunc.PassExecContext?1:0);

            if (hostFunc.PassExecContext)
            {
                hostFunc.ParameterBuffer[0] = this;
            }
            
            //Pass them
            await hostFunc.InvokeAsync(hostFunc.ParameterBuffer, OpStack);
        }
        
        public InstructionBase? Next()
        {
            //Advance to the next instruction first.
            return (++_sequenceIndex < _sequenceCount)
                //Critical path, using direct array access
                ? _sequenceInstructions[_sequenceIndex]
                : null;
        }

        public List<(string, int)> ComputePointerPath()
        {
            Stack<(string, int)> ascent = new();
            int idx = _sequenceIndex;
            
            foreach (var label in Frame.EnumerateLabels())
            {
                var pointer = (label.Instruction.GetMnemonic(), idx);
                ascent.Push(pointer);
            
                idx = label.ContinuationAddress.Index;
                
                switch ((OpCode)label.Instruction)
                {
                    case OpCode.If: ascent.Push(("InstIf", 0));
                        break;
                    case OpCode.Else: ascent.Push(("InstElse", 1));
                        break;
                    case OpCode.Block: ascent.Push(("InstBlock", 0));
                        break;
                    case OpCode.Loop: ascent.Push(("InstLoop", 0));
                        break;
                }
                
            }
            
            ascent.Push(("Function", (int)Frame.Index.Value));

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
        }

        public OpCode GetEndFor()
        {
            if (Frame.LabelCount == 1 && Frame.Label.Instruction.x00 == OpCode.Func)
                return OpCode.Func;    
            return OpCode.Block;
        }
    }
}