using System.Collections.Generic;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class RuntimeAttributes
    {
        public IInstructionFactory InstructionFactory { get; set; } = ReferenceFactory.Factory;
        public double FloatingPointTolerance { get; set; } = 1e-10;
    }

    public class ExecContext
    {
        public delegate string MessageProducer();

        public readonly RuntimeAttributes Attributes;

        private InstructionSequence _currentSequence;
        private int _sequenceIndex;

        public ExecContext(Store store, InstructionSequence seq, RuntimeAttributes? attributes = default)
        {
            Store = store;
            _currentSequence = seq;
            _sequenceIndex = 0;
            Attributes = attributes ?? new RuntimeAttributes();
        }

        public IInstructionFactory InstructionFactory => Attributes.InstructionFactory;

        public Store Store { get; }
        public OpStack OpStack { get; } = new();
        private Stack<Frame> CallStack { get; } = new();

        public Frame Frame => CallStack.Peek();

        public void Assert(bool factIsTrue, MessageProducer message)
        {
            if (!factIsTrue)
                throw new TrapException(message());
        }

        public void PushFrame(Frame frame)
        {
            CallStack.Push(frame);
        }

        public Frame PopFrame()
        {
            return CallStack.Pop();
        }

        private void EnterSequence(InstructionSequence seq) =>
            (_currentSequence, _sequenceIndex) = (seq, 0);

        public void ResumeSequence(InstructionPointer pointer) =>
            (_currentSequence, _sequenceIndex) = (pointer.Sequence, pointer.Index);

        public InstructionPointer GetPointer(int offset = 0) =>
            new(_currentSequence, _sequenceIndex + offset);

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

        // @Spec 4.4.10.1 Function Invocation
        public void Invoke(FuncAddr addr)
        {
            //1.
            Assert(Store.Contains(addr),
                () => $"Failure in Function Invocation. Address does not exist {addr}");
            //2.
            var funcInst = Store[addr];
            switch (funcInst)
            {
                case FunctionInstance wasmFunc:
                    Invoke(wasmFunc);
                    return;
                case HostFunction hostFunc:
                    Invoke(hostFunc);
                    break;
            }
        }

        private void Invoke(FunctionInstance wasmFunc)
        {
            //3.
            var funcType = wasmFunc.Type;
            //4.
            var t = wasmFunc.Definition.Locals;
            //5. *Instructions will be handled in EnterSequence below
            //var seq = wasmFunc.Definition.Body;
            //6.
            Assert(OpStack.Count >= funcType.ParameterTypes.Arity,
                () => $"Function invocation failed. Operand Stack underflow.");
            //7.
            var vals = OpStack.PopResults(funcType.ParameterTypes);
            //8.
            var frame = new Frame(wasmFunc.Module, funcType)
            {
                ContinuationAddress = GetPointer(),
                Locals = new LocalsSpace(funcType.ParameterTypes.Types, t)
            };
            int li = 0;
            int localCount = funcType.ParameterTypes.Arity + t.Length;
            //Load parameters
            while (vals.Count > 0)
            {
                frame.Locals[(LocalIdx)li] = vals.Pop();
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
            var label = new Label(funcType.ResultType, GetPointer(), OpCode.Call)
            {
                StackHeight = OpStack.Count
            };
            frame.Labels.Push(label);
            EnterSequence(wasmFunc.Definition.Body.Instructions);
        }

        private void Invoke(HostFunction hostFunc)
        {
            var funcType = hostFunc.Type;
            var vals = OpStack.PopScalars(funcType.ParameterTypes);
            var results = hostFunc.Invoke(vals);
            foreach (var result in results)
            {
                OpStack.PushValue(new Value(result));
            }
        }

        // @Spec 4.4.10.2. Returning from a function
        public void FunctionReturn()
        {
            //3.
            Assert(OpStack.Count >= Frame.Arity,
                () => $"Function Return failed. Stack did not contain return values");
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

            if (_sequenceIndex >= _currentSequence.Count)
                return null;

            return _currentSequence[_sequenceIndex++];
        }
    }
}