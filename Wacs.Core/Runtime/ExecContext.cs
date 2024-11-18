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
using System.Linq;
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
        private readonly InstructionSequence _hostReturnSequence;
        public readonly RuntimeAttributes Attributes;

        private Stack<Value> _asideVals = new();

        private InstructionSequence _currentSequence;
        private int _sequenceIndex;

        public Dictionary<ushort, ExecStat> Stats = new();

        public ExecContext(Store store, RuntimeAttributes? attributes = default)
        {
            Store = store;
            Attributes = attributes ?? new RuntimeAttributes();

            FramePool = new DefaultObjectPool<Frame>(new StackPoolPolicy<Frame>(), Attributes.MaxCallStack)
                .Prime(Attributes.InitialCallStack);
            
            LocalsDataPool = ArrayPool<Value>.Create(Attributes.MaxFunctionLocals, Attributes.LocalPoolSize);
            
            CallStack = new (Attributes.InitialCallStack);
            LabelStack = new(Attributes.InitialLabelsStack, Attributes.GrowLabelsStack);

            _hostReturnSequence = new InstructionSequence(
                InstructionFactory.CreateInstruction<InstFuncReturn>(OpCode.Func)
            );
            _currentSequence = _hostReturnSequence;
            _sequenceIndex = -1;

            OpStack = new(Attributes.MaxOpStack);
        }

        public Stopwatch ProcessTimer { get; set; } = new();
        public Stopwatch InstructionTimer { get; set; } = new();

        public IInstructionFactory InstructionFactory => Attributes.InstructionFactory;

        public Store Store { get; }
        public OpStack OpStack { get; }

        private ReusableStack<Label> LabelStack { get; }
        private ObjectPool<Frame> FramePool { get; }
        private ArrayPool<Value> LocalsDataPool { get; }
        private Stack<Frame> CallStack { get; }

        public Frame Frame => CallStack.Peek();

        public MemoryInstance DefaultMemory => Store[Frame.Module.MemAddrs[(MemIdx)0]];

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
            locals ??= Array.Empty<ValType>();
            
            var frame = FramePool.Get();
            frame.Module = module;
            frame.Type = type;
            frame.Labels = LabelStack.GetSubStack();
            frame.Index = index;
            frame.ContinuationAddress = GetPointer();
            
            int capacity = type.ParameterTypes.Types.Length + locals.Length;
            var localData = LocalsDataPool.Rent(capacity);
            frame.Locals = new(localData, type.ParameterTypes.Types, locals);
            
            return frame;
        }

        public void PushFrame(Frame frame)
        {
            if (CallStack.Count >= Attributes.MaxCallStack)
                throw new WasmRuntimeException($"Runtime call stack exhausted {CallStack.Count}");
            
            CallStack.Push(frame);
        }

        public InstructionPointer PopFrame()
        {
            var frame = CallStack.Pop();
            frame.Labels.Drop();
            if (frame.Locals.Data != null)
                frame.ReturnLocals(LocalsDataPool);

            var address = frame.ContinuationAddress;
            FramePool.Return(frame);
            return address;
        }

        public void ResetStack(Label label)
        {
            while (OpStack.Count > label.StackHeight)
            {
                OpStack.PopAny();
            }
        }

        public void FlushCallStack()
        {
            while (CallStack.Count > 0)
                PopFrame();
            while (OpStack.Count > 0)
                OpStack.PopAny();

            _currentSequence = _hostReturnSequence;
            _sequenceIndex = -1;
        }

        private void EnterSequence(InstructionSequence seq) =>
            (_currentSequence, _sequenceIndex) = (seq, -1);

        public void ResumeSequence(InstructionPointer pointer) =>
            (_currentSequence, _sequenceIndex) = (pointer.Sequence, pointer.Index);

        public void FastForwardSequence()
        {
            //Go to penultimate instruction since we pre-increment on pointer advance.
            _sequenceIndex = _currentSequence.Length - 2;
        }

        public void RewindSequence()
        {
            //Go back to the first instruction in the sequence
            _sequenceIndex = -1;
        }

        public InstructionPointer GetPointer() => new(_currentSequence, _sequenceIndex);

        // @Spec 4.4.9.1. Enter Block
        public void EnterBlock(Block block, ResultType resultType, ByteCode inst, Stack<Value> vals)
        {
            OpStack.Push(vals);
            var label = Frame.ReserveLabel();
            label.Set(resultType, this.GetPointer(), inst, OpStack.Count);
            Frame.Labels.Push(label);
            //Sets the Pointer to the start of the block sequence
            EnterSequence(block.Instructions);
        }

        // @Spec 4.4.9.2. Exit Block
        public void ExitBlock()
        {
            var addr = Frame.PopLabel();
            // We manage separate stacks, so we don't need to relocate the operands
            // var vals = OpStack.PopResults(label.Type);
            ResumeSequence(addr);
        }

        // @Spec 4.4.10.1 Function Invocation
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
                    Invoke(wasmFunc, wasmFunc.Index);
                    return;
                case HostFunction hostFunc:
                    Invoke(hostFunc);
                    break;
            }
        }

        private void Invoke(FunctionInstance wasmFunc, FuncIdx idx)
        {
            //3.
            var funcType = wasmFunc.Type;
            //4.
            var t = wasmFunc.Definition.Locals;
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
                frame.Locals.Set((LocalIdx)li, _asideVals.Pop());
                li += 1;
            }
            //Set the Locals to default
            for (int ti = 0; li < localCount; ++li, ++ti)
            {
                frame.Locals.Set((LocalIdx)li, new Value(t[ti]));
            }

            //9.
            PushFrame(frame);
            
            //10.
            var label = frame.ReserveLabel();
            label.Set(funcType.ResultType, GetPointer(), OpCode.Expr, OpStack.Count);
            frame.Labels.Push(label);
            EnterSequence(wasmFunc.Definition.Body.Instructions);
        }

        private void Invoke(HostFunction hostFunc)
        {
            var funcType = hostFunc.Type;

            //Write the ExecContext to the first parameter if needed
            var paramBuf = hostFunc.GetParameterBuf(this);
            
            //Fetch the parameters
            OpStack.PopScalars(funcType.ParameterTypes, paramBuf);

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

        public IInstruction? Next()
        {
            if (_currentSequence == _hostReturnSequence)
                return null;

            //Advance to the next instruction first.
            ++_sequenceIndex;
            
            if (_sequenceIndex >= _currentSequence.Count)
                return null;

            return _currentSequence[_sequenceIndex];
        }

        public List<(string, int)> ComputePointerPath()
        {
            Stack<(string, int)> ascent = new();
            int idx = _sequenceIndex;
            
            foreach (var label in Frame.Labels)
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
            if (Frame.Labels.Count == 1 && Frame.Label.Instruction.x00 == OpCode.Expr)
                return OpCode.Func;    
            return OpCode.Block;
        }
    }
}