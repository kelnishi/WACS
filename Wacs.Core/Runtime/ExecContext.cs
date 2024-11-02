using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class RuntimeAttributes
    {
        public bool Live = true;
        public IInstructionFactory InstructionFactory { get; set; } = ReferenceFactory.Factory;
        public double FloatingPointTolerance { get; set; } = 1e-10;
    }

    public struct ExecStat
    {
        public long duration;
        public long count;
    }

    public class ExecContext
    {
        public readonly RuntimeAttributes Attributes;

        private Stack<Value> _asideVals = new();

        private InstructionSequence _currentSequence;
        private int _sequenceIndex;

        public Dictionary<ushort, ExecStat> Stats = new();

        public ExecContext(Store store, InstructionSequence seq, RuntimeAttributes? attributes = default)
        {
            Store = store;
            _currentSequence = seq;
            _sequenceIndex = -1;
            Attributes = attributes ?? new RuntimeAttributes();
        }

        public Stopwatch ProcessTimer { get; set; } = new();
        public Stopwatch InstructionTimer { get; set; } = new();

        public IInstructionFactory InstructionFactory => Attributes.InstructionFactory;

        public Store Store { get; }
        public OpStack OpStack { get; } = new();
        private Stack<Frame> CallStack { get; } = new();

        public Frame Frame => CallStack.Peek();

        public MemoryInstance DefaultMemory => Store[Frame.Module.MemAddrs[(MemIdx)0]];

        [Conditional("STRICT_EXECUTION")]
        public void Assert(bool assertion, string message)
        {
            if (!assertion)
                throw new TrapException(message);
        }

        public void PushFrame(Frame frame)
        {
            CallStack.Push(frame);
        }

        public Frame PopFrame()
        {
            return CallStack.Pop();
        }

        public void ResetStack(Label label)
        {
            while (OpStack.Count > label.StackHeight)
            {
                OpStack.PopAny();
            }
        }

        private void EnterSequence(InstructionSequence seq) =>
            (_currentSequence, _sequenceIndex) = (seq, -1);

        public void ResumeSequence(InstructionPointer pointer) =>
            (_currentSequence, _sequenceIndex) = (pointer.Sequence, pointer.Index);

        public InstructionPointer GetPointer() => new(_currentSequence, _sequenceIndex);

        // @Spec 4.4.9.1. Enter Block
        public void EnterBlock(Label label, Block block, Stack<Value> vals)
        {
            label.StackHeight = OpStack.Count;
            OpStack.Push(vals);
            Frame.Labels.Push(label);
            //Sets the Pointer to the start of the block sequence
            EnterSequence(block.Instructions);
        }

        // @Spec 4.4.9.2. Exit Block
        public void ExitBlock()
        {
            var label = Frame.Labels.Pop();
            // We manage separate stacks, so we don't need to relocate the operands
            // var vals = OpStack.PopResults(label.Type);
            ResumeSequence(label.ContinuationAddress);
        }

        public void EndLoop()
        {
            var label = Frame.Labels.Pop();
            ResumeSequence(label.ContinuationAddress.Previous);
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
            var frame = new Frame(wasmFunc.Module, funcType)
            {
                ContinuationAddress = GetPointer(),
                Locals = new LocalsSpace(funcType.ParameterTypes.Types, t),
                Index = idx
            };
            int li = 0;
            int localCount = funcType.ParameterTypes.Arity + t.Length;
            //Load parameters
            while (_asideVals.Count > 0)
            {
                frame.Locals[(LocalIdx)li] = _asideVals.Pop();
                li += 1;
            }
            //Set the Locals to default
            for (int ti = 0; li < localCount; ++li, ++ti)
            {
                frame.Locals[(LocalIdx)li] = new Value(t[ti]);
            }

            //9.
            PushFrame(frame);
            //10.
            var label = new Label(funcType.ResultType, GetPointer(), OpCode.Expr)
            {
                StackHeight = OpStack.Count,
            };
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
            var frame = PopFrame();
            //7. split stack, values left in place 
            //8.
            ResumeSequence(frame.ContinuationAddress);
        }

        public IInstruction? Next()
        {
            if (_currentSequence.IsEmpty)
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

        public void SetFrame(Frame frame)
        {
            while (CallStack.Count > 0)
                CallStack.Pop();
            CallStack.Push(frame);
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