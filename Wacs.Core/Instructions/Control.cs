using System;
using System.IO;
using System.Linq;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// @Spec 2.4.8. Control Instructions
// @Spec 5.4.1 Control Instructions
namespace Wacs.Core.Instructions
{
    //0x00
    public class InstUnreachable : InstructionBase
    {
        public static readonly InstUnreachable Inst = new();
        public override ByteCode Op => OpCode.Unreachable;

        // @Spec 3.3.8.2 unreachable
        public override void Validate(WasmValidationContext context)
        {
        }

        // @Spec 4.4.8.2. unreachable
        public override void Execute(ExecContext context) =>
            throw new TrapException("unreachable");
    }

    //0x01
    public class InstNop : InstructionBase
    {
        public static readonly InstNop Inst = new();
        public override ByteCode Op => OpCode.Nop;

        // @Spec 3.3.8.1. nop
        public override void Validate(WasmValidationContext context)
        {
        }

        // @Spec 4.4.8.1. nop
        public override void Execute(ExecContext context)
        {
        }
    }

    //0x02
    public class InstBlock : InstructionBase, IBlockInstruction
    {
        public override ByteCode Op => OpCode.Block;
        private Block Block { get; set; } = null!;

        public BlockType Type => Block.Type;

        public int Count => 1;

        public int Size => 1 + Block.Size;
        public InstructionSequence GetBlock(int idx) => Block.Instructions;

        // @Spec 3.3.8.3 block
        public override void Validate(WasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(Block.Type);
                context.Assert(funcType != null, () => $"Invalid BlockType: {Block.Type}");
                
                var label = new Label(funcType!.ResultType, new InstructionPointer(), OpCode.Block);
                context.ControlStack.Frame.Labels.Push(label);
                //Check the parameters [t1*]
                context.OpStack.ValidateStack(funcType.ParameterTypes, false);
                //Clean stack with params
                context.NewOpStack(funcType.ParameterTypes);
                //Validate
                context.ValidateBlock(Block);
                //Check the result [t2*]
                if (context.Reachability) {
                    context.OpStack.ValidateStack(funcType.ResultType, false);
                    if (context.OpStack.Height > 0)
                        throw new InvalidDataException("Why do we still have operands?");
                }

                //Restore stack with results
                context.FreeOpStack(funcType.ResultType);
                context.ControlStack.Frame.Labels.Pop();
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    () => $"Instruction block was invalid. BlockType {Block.Type} did not exist in the Context.");
            }
        }

        // @Spec 4.4.8.3. block
        public override void Execute(ExecContext context) => ExecuteInstruction(context, Block);

        public static void ExecuteInstruction(ExecContext context, Block block)
        {
            try
            {
                //2,3
                var funcType = context.Frame.Module.Types.ResolveBlockType(block.Type);
                //4.
                var label = new Label(funcType!.ResultType, context.GetPointer(), OpCode.Block);
                //5.
                context.Assert(() => context.OpStack.Count >= funcType.ParameterTypes.Length,
                    () => $"Instruction block failed. Operand Stack underflow.");
                //6. 
                var vals = context.OpStack.PopResults(funcType.ParameterTypes);
                //7.
                context.EnterBlock(label, block, vals);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                context.Assert(() => false,
                    () => $"Instruction block failed. BlockType {block.Type} did not exist in the Context.");
            }
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            Block = new Block(type: Block.ParseBlockType(reader))
            {
                Instructions = new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction,
                    IInstruction.IsEnd))
            };
            return this;
        }
    }

    //0x03
    public class InstLoop : InstructionBase, IBlockInstruction
    {
        public override ByteCode Op => OpCode.Loop;
        private Block Block { get; set; } = null!;

        public BlockType Type => Block.Type;

        public int Count => 1;

        public int Size => 1 + Block.Size;
        public InstructionSequence GetBlock(int idx) => Block.Instructions;

        // @Spec 3.3.8.4. loop
        public override void Validate(WasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(Block.Type);
                context.Assert(funcType != null, () => $"Invalid BlockType: {Block.Type}");

                var label = new Label(funcType!.ResultType, new InstructionPointer(), OpCode.Block);
                context.ControlStack.Frame.Labels.Push(label);
                //Check the parameters [t1*]
                context.OpStack.ValidateStack(funcType.ParameterTypes, false);
                //Clean stack with params
                context.NewOpStack(funcType.ParameterTypes);
                //Validate
                context.ValidateBlock(Block);
                //Check the result [t2*]
                if (context.Reachability) {
                    context.OpStack.ValidateStack(funcType.ResultType, false);
                    if (context.OpStack.Height > 0)
                        throw new InvalidDataException("Why do we still have operands?");
                }
                //Restore stack with results
                context.FreeOpStack(funcType.ResultType);
                context.ControlStack.Frame.Labels.Pop();
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    () => $"Instruciton loop invalid. BlockType {Block.Type} did not exist in the Context.");
            }
        }

        // @Spec 4.4.8.4. loop
        public override void Execute(ExecContext context)
        {
            try
            {
                //2,3
                var funcType = context.Frame.Module.Types.ResolveBlockType(Block.Type);
                //4.
                var label = new Label(funcType!.ResultType, context.GetPointer(-1), OpCode.Loop);
                //5.
                context.Assert(() => context.OpStack.Count >= funcType.ParameterTypes.Length,
                    () => $"Instruction loop failed. Operand Stack underflow.");
                //6. 
                var vals = context.OpStack.PopResults(funcType.ParameterTypes);
                //7.
                context.EnterBlock(label, Block, vals);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                context.Assert(() => false,
                    () => $"Instruction loop failed. BlockType {Block.Type} did not exist in the Context.");
            }
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            Block = new Block(type: Block.ParseBlockType(reader))
            {
                Instructions = new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction,
                    IInstruction.IsEnd))
            };
            return this;
        }
    }

    //0x04
    public class InstIf : InstructionBase, IBlockInstruction
    {
        public override ByteCode Op => OpCode.If;
        private Block IfBlock { get; set; } = Block.Empty;
        private Block ElseBlock { get; set; } = Block.Empty;

        public BlockType Type => IfBlock.Type;

        public int Count => ElseBlock.Size == 0 ? 1 : 2;

        public int Size => 1 + IfBlock.Size + ElseBlock.Size;
        public InstructionSequence GetBlock(int idx) => idx == 0 ? IfBlock.Instructions : ElseBlock.Instructions;

        // @Spec 3.3.8.5 if
        public override void Validate(WasmValidationContext context)
        {
            try
            {
                //Pop the predicate
                context.OpStack.PopI32();

                int height = context.OpStack.Height;

                var funcType = context.Types.ResolveBlockType(IfBlock.Type);
                context.Assert(funcType != null, () => $"Invalid BlockType: {IfBlock.Type}");

                var label = new Label(funcType!.ResultType, new InstructionPointer(), OpCode.Block);
                context.ControlStack.Frame.Labels.Push(label);
                
                //Check the parameters [t1*]
                context.OpStack.ValidateStack(funcType.ParameterTypes, false);
                //Load the clean stack with parameters
                context.NewOpStack(funcType.ParameterTypes);
                //Validate
                context.ValidateBlock(IfBlock, 0);
                //Check results
                if (context.Reachability) {
                    context.OpStack.ValidateStack(funcType.ResultType, keep: false);
                    if (context.OpStack.Height > 0)
                        throw new InvalidDataException("Why do we still have operands?");
                }

                //Restore the stack with results
                context.FreeOpStack(funcType.ResultType);
                
                if (ElseBlock.Size == 0)
                {
                    context.ControlStack.Frame.Labels.Pop();
                    return;
                }
                
                //For an Else case, do it again
                //Load the clean stack with parameters
                context.NewOpStack(funcType.ParameterTypes);
                //Validate
                context.ValidateBlock(IfBlock, 0);
                //Check results
                if (context.Reachability) {
                    context.OpStack.ValidateStack(funcType.ResultType, keep: false);
                    if (context.OpStack.Height > 0)
                        throw new InvalidDataException("Why do we still have operands?");
                }
                //Restore the stack, results are already on the stack from the first block
                context.FreeOpStack(ResultType.Empty);
                context.ControlStack.Frame.Labels.Pop();
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    () => $"Instruciton loop invalid. BlockType {IfBlock.Type} did not exist in the Context.");
            }
        }

        // @Spec 4.4.8.5. if
        public override void Execute(ExecContext context)
        {
            int c = context.OpStack.PopI32();
            if (c != 0)
            {
                InstBlock.ExecuteInstruction(context, IfBlock);
            }
            else
            {
                InstBlock.ExecuteInstruction(context, ElseBlock);
            }
        }


        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            IfBlock = new Block(type: Block.ParseBlockType(reader))
            {
                Instructions = new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction,
                    IInstruction.IsElseOrEnd))
            };

            if (IfBlock.Instructions.HasExplicitEnd)
            {
                ElseBlock.Instructions = InstructionSequence.Empty;
            }
            else if (IfBlock.Instructions.EndsWithElse)
            {
                ElseBlock = new Block(type: IfBlock.Type)
                {
                    Instructions = new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction,
                        IInstruction.IsEnd))
                };
                if (ElseBlock.Size == 0)
                    throw new InvalidDataException($"Explicit Else block contained no instructions.");
            }
            else
            {
                throw new InvalidDataException($"If block did not terminate correctly.");
            }

            return this;
        }
    }

    //0x05
    public class InstElse : InstEnd
    {
        public new static readonly InstElse Inst = new();
        public override ByteCode Op => OpCode.Else;
    }

    //0x0B
    public class InstEnd : InstructionBase
    {
        public static readonly InstEnd Inst = new();
        public override ByteCode Op => OpCode.End;

        public override void Validate(WasmValidationContext context)
        {
        }

        public override void Execute(ExecContext context)
        {
            var label = context.Frame.Label;
            switch (label.Instruction.x00)
            {
                case OpCode.Block:
                case OpCode.Loop:
                    context.ExitBlock();
                    break;
                case OpCode.Call:
                    context.FunctionReturn();
                    break;
                default:
                    //Do nothing
                    break;
            }
        }
    }

    //0x0C
    public class InstBranch : InstructionBase, IBranchInstruction
    {
        public override ByteCode Op => OpCode.Br;

        public LabelIdx L { get; internal set; }

        // @Spec 3.3.8.6. br l
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.ControlStack.Frame.Contains(L),
                () => $"Instruction br invalid. Context did not contain Label {L}");
            var label = context.ControlStack.Frame[L];

            //Validate results, but leave them on the stack
            context.OpStack.ValidateStack(label.Type, keep: true);
            context.Reachability = false;
        }

        // @Spec 4.4.8.6. br l
        public override void Execute(ExecContext context) => ExecuteInstruction(context, L);

        public static void ExecuteInstruction(ExecContext context, LabelIdx labelIndex)
        {
            //1.
            context.Assert(() => context.Frame.Labels.Count > (int)labelIndex.Value,
                () => $"Instruction br failed. Context did not contain Label {labelIndex}");
            //2.
            var label = context.Frame[labelIndex];
            //3.
            int n = label.Arity;
            //4.
            context.Assert(() => context.OpStack.Count >= n,
                () => $"Instruction br failed. Not enough values on the stack.");
            //5.
            var vals = context.OpStack.PopResults(label.Type);
            //6.
            while (context.OpStack.Count > label.StackHeight)
            {
                context.OpStack.PopAny();
            }

            Label? sl = null;
            for (int i = -1, l = (int)labelIndex.Value; i < l; ++i)
            {
                sl = context.Frame.Labels.Pop();
            }

            context.Assert(() => label == sl,
                () => $"Instruction br failed. Failure in stack management.");
            //7.
            context.OpStack.Push(vals);
            //8.
            context.ResumeSequence(label.ContinuationAddress);
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            L = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(int depth) => $"{base.RenderText(depth)} {L.Value} (;@{depth - L.Value};)";
    }

    //0x0D
    public class InstBranchConditional : InstructionBase, IBranchInstruction
    {
        public override ByteCode Op => OpCode.BrIf;

        public LabelIdx L { get; internal set; }

        // @Spec 3.3.8.7. br_if
        public override void Validate(WasmValidationContext context)
        {
            context.OpStack.PopI32();
            context.Assert(context.ControlStack.Frame.Contains(L),
                () => $"Instruction br_if invalid. Context did not contain Label {L}");
            var label = context.ControlStack.Frame[L];
            
            //Validate results, but leave them on the stack
            context.OpStack.ValidateStack(label.Type, keep: true);
        }

        // @Spec 4.4.8.7. br_if
        public override void Execute(ExecContext context)
        {
            int c = context.OpStack.PopI32();
            if (c != 0)
            {
                InstBranch.ExecuteInstruction(context, L);
            }
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            L = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(int depth) => $"{base.RenderText(depth)} {L.Value} (;@{depth - L.Value};)";
    }

    //0x0E
    public class InstBranchTable : InstructionBase, IBranchInstruction
    {
        public override ByteCode Op => OpCode.BrTable;

        private LabelIdx[] Ls { get; set; } = null!;
        private LabelIdx Ln { get; set; }

        // @Spec 3.3.8.8. br_table
        public override void Validate(WasmValidationContext context)
        {
            context.OpStack.PopI32();
            context.Assert(context.ControlStack.Frame.Contains(Ln),
                () => $"Instruction br_table invalid. Context did not contain Label {Ln}");
            foreach (var lidx in Ls)
            {
                context.Assert(context.ControlStack.Frame.Contains(lidx),
                    () => $"Instruction br_table invalid. Context did not contain Label {lidx}");
            }

            var labelN = context.ControlStack.Frame[Ln];
            var typeN = labelN.Type;
            foreach (var lidx in Ls)
            {
                var label = context.ControlStack.Frame[lidx];
                var type = label.Type;
                context.Assert(typeN.Matches(type),
                    () => $"Instruction br_table failed. Table types are incongruent.");
            }
            
            context.OpStack.ValidateStack(labelN.Type, keep: true);
        }

        /// <summary>
        /// @Spec 4.4.8.8. br_table
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(ExecContext context)
        {
            //2.
            int i = context.OpStack.PopI32();
            //3.
            if (i < Ls.Length)
            {
                var label = Ls[i];
                InstBranch.ExecuteInstruction(context, label);
            }
            //4.
            else
            {
                InstBranch.ExecuteInstruction(context, Ln);
            }
        }

        private static LabelIdx ParseLabelIndex(BinaryReader reader) =>
            (LabelIdx)reader.ReadLeb128_u32();

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            Ls = reader.ParseVector(ParseLabelIndex);
            Ln = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(int depth) => 
            $"{base.RenderText(depth)} {string.Join(" ", Ls.Select(idx => idx.Value).Select(v => $"{v} (;@{depth - v};)"))} {Ln.Value} (;@{depth - Ln.Value};)";
    }

    //0x0F
    public class InstReturn : InstructionBase
    {
        public static readonly InstReturn Inst = new();
        public override ByteCode Op => OpCode.Return;

        // @Spec 3.3.8.9. return
        public override void Validate(WasmValidationContext context)
        {
            var returnType = context.ControlStack.Frame.Type.ResultType;
            //keep the results for the block or function to validate
            context.OpStack.ValidateStack(returnType);
            context.Reachability = false;
            
            //Push the results to the return stack
            foreach (var type in returnType.Types)
            {
                context.ReturnStack.PushType(type);
            }
        }

        // @Spec 4.4.8.9. return
        public override void Execute(ExecContext context)
        {
            context.Assert(() => context.OpStack.Count >= context.Frame.Arity,
                () => $"Instruction return failed. Operand stack underflow");
            //We're managing separate stacks, so we won't need to shift the operands
            // var vals = context.OpStack.PopResults(context.Frame.Type.ResultType);
            var frame = context.PopFrame();
            context.ResumeSequence(frame.ContinuationAddress);
        }
    }

    //0x10
    public class InstCall : InstructionBase
    {
        public override ByteCode Op => OpCode.Call;

        private FuncIdx X { get; set; }

        /// <summary>
        /// @Spec 3.3.8.10. call
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Funcs.Contains(X),
                () => $"Instruction call was invalid. Function {X} was not in the Context.");
            var func = context.Funcs[X];
            var type = context.Types[func.TypeIndex];
            context.OpStack.ValidateStack(type.ParameterTypes, false);
            context.OpStack.Push(type.ResultType);
        }

        // @Spec 4.4.8.10. call
        public override void Execute(ExecContext context)
        {
            context.Assert(() => context.Frame.Module.FuncAddrs.Contains(X),
                () => $"Instruction call failed. Function address for {X} was not in the Context.");
            var a = context.Frame.Module.FuncAddrs[X];
            context.Invoke(a);
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            X = (FuncIdx)reader.ReadLeb128_u32();
            return this;
        }

        public IInstruction Immediate(FuncIdx value)
        {
            X = value;
            return this;
        }

        public override string RenderText(int depth) => $"{base.RenderText(depth)} {X.Value}";
    }

    //0x11
    public class InstCallIndirect : InstructionBase
    {
        public override ByteCode Op => OpCode.CallIndirect;

        private TypeIdx Y { get; set; }
        private TableIdx X { get; set; }

        /// <summary>
        /// @Spec 3.3.8.11. call_indirect
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                () => $"Instruction call_indirect was invalid. Table {X} was not in the Context.");
            var tableType = context.Tables[X];
            context.Assert(tableType.ElementType == ReferenceType.Funcref,
                () => $"Instruction call_indirect was invalid. Table type was not funcref");
            context.Assert(context.Types.Contains(Y),
                () => $"Instruction call_indirect was invalid. Function type {Y} was not in the Context.");
            var funcType = context.Types[Y];

            context.OpStack.PopI32();
            context.OpStack.ValidateStack(funcType.ParameterTypes, false);
            context.OpStack.Push(funcType.ResultType);
        }

        // @Spec 4.4.8.11. call_indirect
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert(() => context.Frame.Module.TableAddrs.Contains(X),
                () => $"Instruction call_indirect failed. Table {X} was not in the Context.");
            //3.
            var ta = context.Frame.Module.TableAddrs[X];
            //4.
            context.Assert(() => context.Store.Contains(ta),
                () => $"Instruction call_indirect failed. TableInstance {ta} was not in the Store.");
            //5.
            var tab = context.Store[ta];
            //6.
            context.Assert(() => context.Frame.Module.Types.Contains(Y),
                () => $"Instruction call_indirect failed. Function Type {Y} was not in the Context.");
            //7.
            var ftExpect = context.Frame.Module.Types[Y];
            //8.
            context.Assert(() => context.OpStack.Peek().IsI32,
                () => $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            int i = context.OpStack.PopI32();
            //10.
            if (i >= tab.Elements.Count)
                throw new TrapException($"Instruction call_indirect could not find element {i}");
            //11. ???
            var r = tab.Elements[i];
            //12.
            if (r.IsNullRef)
                throw new TrapException($"Instruction call_indirect NullReference.");
            //13.
            context.Assert(() => r.Type == ValType.Funcref,
                () => $"Instruction call_indirect failed. Element was not a FuncRef");
            //14. ???
            var a = context.Frame.Module.FuncAddrs[r.FuncIdx];
            //15.
            context.Assert(() => context.Store.Contains(a),
                () => $"Instruction call_indirect failed. Validation of table mutation failed.");
            //16.
            var funcInst = context.Store[a];
            //17.
            var ftActual = funcInst.Type;
            //18.
            if (!ftExpect.Matches(ftActual))
                throw new TrapException($"Instruction call_indirect failed. Expected FunctionType differed.");
            //19.
            context.Invoke(a);
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            Y = (TypeIdx)reader.ReadLeb128_u32();
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(int depth) => 
            $"{base.RenderText(depth)}{(X.Value == 0 ? "" : $" {X.Value}")} (type {Y.Value})";
    }
}