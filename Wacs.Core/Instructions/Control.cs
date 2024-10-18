using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

// @Spec 2.4.8. Control Instructions
// @Spec 5.4.1 Control Instructions
namespace Wacs.Core.Instructions
{
    //0x00
    public class InstUnreachable : InstructionBase
    {
        public override OpCode OpCode => OpCode.Unreachable;
        
        // @Spec 3.3.8.2 unreachable
        public override void Validate(WasmValidationContext context) { }
        
        // @Spec 4.4.8.2. unreachable
        public override void Execute(ExecContext context) =>
            throw new TrapException("unreachable");
        
        public class UnreachableException : Exception { }
        
        public static readonly InstUnreachable Inst = new();
    }
    
    //0x01
    public class InstNop : InstructionBase
    {
        public override OpCode OpCode => OpCode.Nop;
        
        // @Spec 3.3.8.1. nop
        public override void Validate(WasmValidationContext context) { }
        // @Spec 4.4.8.1. nop
        public override void Execute(ExecContext context) { }
        
        public static readonly InstNop Inst = new();
    }
    
    //0x02
    public class InstBlock : InstructionBase
    {
        public override OpCode OpCode => OpCode.Block;
        public Block Block { get; internal set; } = null!;

        // @Spec 3.3.8.3 block
        public override void Validate(WasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(Block.Type);
                var label = new Label(funcType.ResultType, new(), OpCode.Block);
                context.ControlStack.Frame.Labels.Push(label);
                
                //Check the parameters [t1*]
                context.OpStack.ValidateStack(funcType.ParameterTypes);
                
                //Advance through the instructions
                foreach (var inst in Block.Instructions)
                {
                    inst.Validate(context);
                }

                if (context.Reachability)
                {
                    context.OpStack.ValidateStack(funcType.ResultType);
                }
                else
                {
                    context.Reachability = true;
                }
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    ()=>$"Instruction block was invalid. BlockType {Block.Type} did not exist in the Context.");
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
                var label = new Label(funcType.ResultType, context.GetPointer(1), OpCode.Block);
                //5.
                context.Assert(context.OpStack.Count >= funcType.ParameterTypes.Length,
                    ()=>$"Instruction block failed. Operand Stack underflow.");
                //6. 
                var vals = context.OpStack.PopResults(funcType.ParameterTypes);
                //7.
                context.EnterBlock(label, block, vals);

            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                context.Assert(false,
                    ()=>$"Instruction block failed. BlockType {block.Type} did not exist in the Context.");   
            }
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            Block = new(type:Block.ParseBlockType(reader)) {
                Instructions = new InstructionSequence(reader.ParseUntil(InstructionParser.Parse,
                    InstructionParser.IsEnd))
            };
            return this;
        }
    }
    
    //0x03
    public class InstLoop : InstructionBase
    {
        public override OpCode OpCode => OpCode.Loop;

        // @Spec 3.3.8.4. loop
        public override void Validate(WasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(Block.Type);
                var label = new Label(funcType.ResultType, new(), OpCode.Block);
                context.ControlStack.Frame.Labels.Push(label);
                
                //Check the parameters [t1*]
                context.OpStack.ValidateStack(funcType.ParameterTypes);
                
                //Advance through the instructions
                foreach (var inst in Block.Instructions)
                {
                    inst.Validate(context);
                }

                if (context.Reachability)
                {
                    context.OpStack.ValidateStack(funcType.ResultType);
                }
                else
                {
                    context.Reachability = true;
                }
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    ()=>$"Instruciton loop invalid. BlockType {Block.Type} did not exist in the Context.");
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
                var label = new Label(funcType.ResultType, context.GetPointer(), OpCode.Block);
                //5.
                context.Assert(context.OpStack.Count >= funcType.ParameterTypes.Length,
                    ()=>$"Instruction loop failed. Operand Stack underflow.");
                //6. 
                var vals = context.OpStack.PopResults(funcType.ParameterTypes);
                //7.
                context.EnterBlock(label, Block, vals);

            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                context.Assert(false,
                    ()=>$"Instruction loop failed. BlockType {Block.Type} did not exist in the Context.");   
            }
        }
        
        public Block Block { get; internal set; } = null!;

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            Block = new(type:Block.ParseBlockType(reader)) {
                Instructions = new InstructionSequence(reader.ParseUntil(InstructionParser.Parse,
                    InstructionParser.IsEnd))
            };
            return this;
        }
    }
    
    //0x04
    public class InstIf : InstructionBase
    {
        public override OpCode OpCode => OpCode.If;
        public Block IfBlock { get; internal set; } = Block.Empty;
        public Block ElseBlock { get; internal set; } = Block.Empty;
        
        // @Spec 3.3.8.5 if
        public override void Validate(WasmValidationContext context)
        {
            try
            {
                //Pop the predicate
                context.OpStack.PopI32();
                
                var funcType = context.Types.ResolveBlockType(IfBlock.Type);
                var label = new Label(funcType.ResultType, new(), OpCode.Block);
                context.ControlStack.Frame.Labels.Push(label);
                
                //Check the parameters [t1*]
                context.OpStack.ValidateStack(funcType.ParameterTypes);
                
                //Advance through the instructions
                foreach (var inst in IfBlock.Instructions)
                {
                    inst.Validate(context);
                }

                if (context.Reachability)
                {
                    context.OpStack.ValidateStack(funcType.ResultType, keep: ElseBlock.IsEmpty);

                    if (!ElseBlock.IsEmpty)
                    {
                        foreach (var type in funcType.ParameterTypes.Types)
                        {
                            context.OpStack.PushType(type);
                        }
                    }
                }
                else
                {
                    context.Reachability = true;
                }

                if (ElseBlock.IsEmpty) 
                    return;
                
                foreach (var inst in ElseBlock.Instructions)
                {
                    inst.Validate(context);
                }

                if (context.Reachability)
                {
                    context.OpStack.ValidateStack(funcType.ResultType);
                }
                else
                {
                    context.Reachability = true;
                }
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    ()=>$"Instruciton loop invalid. BlockType {IfBlock.Type} did not exist in the Context.");
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
            IfBlock = new(type: Block.ParseBlockType(reader)) {
                Instructions = new InstructionSequence(reader.ParseUntil(InstructionParser.Parse,
                    InstructionParser.IsElseOrEnd))
            };

            if (IfBlock.Instructions.HasExplicitEnd)
            {
                ElseBlock.Instructions = InstructionSequence.Empty;
            }
            else if (IfBlock.Instructions.EndsWithElse)
            {
                IfBlock.Instructions.SwapElseEnd();
                ElseBlock = new(type: IfBlock.Type) {
                    Instructions = new InstructionSequence(reader.ParseUntil(InstructionParser.Parse,
                        InstructionParser.IsEnd))
                };
            }
            return this;
        }
    }

    //0x05
    public class InstElse : InstructionBase
    {
        public override OpCode OpCode => OpCode.Else;
        
        // @Spec 3.3.8.5. else
        public override void Validate(WasmValidationContext context) {}
        
        // @Spec 4.4.8.5. else
        public override void Execute(ExecContext context) {}
        
        public static readonly InstElse Inst = new();
    }
    
    //0x0B
    public class InstEnd : InstructionBase
    {
        public override OpCode OpCode => OpCode.End;
        
        public override void Validate(WasmValidationContext context) {}

        public override void Execute(ExecContext context)
        {
            var label = context.Frame.Label;
            switch (label.Instruction)
            {
                case OpCode.Block:
                case OpCode.Loop:
                    context.ExitBlock();
                    break;
                case OpCode.Call:
                    context.FunctionReturn();
                    break;
                default:
                    break;
            }

        }
        
        public static readonly InstEnd Inst = new();
    }
    
    //0x0C
    public class InstBranch : InstructionBase
    {
        public override OpCode OpCode => OpCode.Br;
        
        public LabelIdx L { get; internal set; }
        
        // @Spec 3.3.8.6. br l
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.ControlStack.Frame.Contains(L),
                ()=>$"Instruction br invalid. Context did not contain Label {L}");
            var label = context.ControlStack.Frame[L];
            foreach (var type in label.Type.Types.Reverse())
            {
                context.OpStack.PopType(type);
            }
        }
        
        // @Spec 4.4.8.6. br l
        public override void Execute(ExecContext context) => ExecuteInstruction(context, L);
        public static void ExecuteInstruction(ExecContext context, LabelIdx labelIndex)
        {
            //1.
            context.Assert(context.Frame.Labels.Count > (int)labelIndex.Value,
                ()=>$"Instruction br failed. Context did not contain Label {labelIndex}");
            //2.
            var label = context.Frame[labelIndex];
            //3.
            int n = label.Arity;
            //4.
            context.Assert(context.OpStack.Count >= n,
                ()=>$"Instruction br failed. Not enough values on the stack.");
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
            context.Assert(label == sl,
                ()=>$"Instruction br failed. Failure in stack management.");
            //7.
            context.OpStack.Push(vals);
            //8.
            context.ResumeSequence(label.ContinuationAddress);
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader) {
            L = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0x0D
    public class InstBranchConditional : InstructionBase
    {
        public override OpCode OpCode => OpCode.BrIf;
        
        public LabelIdx L { get; internal set; }

        // @Spec 3.3.8.7. br_if
        public override void Validate(WasmValidationContext context)
        {
            context.OpStack.PopI32();
            context.Assert(context.ControlStack.Frame.Contains(L),
                ()=>$"Instruction br_if invalid. Context did not contain Label {L}");
            var label = context.ControlStack.Frame[L];
            foreach (var type in label.Type.Types.Reverse())
            {
                context.OpStack.PopType(type);
            }
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
        public override IInstruction Parse(BinaryReader reader) {
            L = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0x0E
    public class InstBranchTable : InstructionBase
    {
        public override OpCode OpCode => OpCode.BrTable;
        
        public LabelIdx[] Ls { get; internal set; } = null!;
        public LabelIdx Ln { get; internal set; }

        // @Spec 3.3.8.8. br_table
        public override void Validate(WasmValidationContext context)
        {
            context.OpStack.PopI32();
            context.Assert(context.ControlStack.Frame.Contains(Ln),
                ()=>$"Instruction br_table invalid. Context did not contain Label {Ln}");
            foreach (var lidx in Ls)
            {
                context.Assert(context.ControlStack.Frame.Contains(lidx),
                    ()=>$"Instruction br_table invalid. Context did not contain Label {lidx}");
            }

            var labelN = context.ControlStack.Frame[Ln];
            var typeN = labelN.Type;
            foreach (var lidx in Ls)
            {
                var label = context.ControlStack.Frame[lidx];
                var type = label.Type;
                context.Assert(typeN.Matches(type),
                    ()=>$"Instruction br_table failed. Table types are incongruent.");
            }
            
            foreach (var type in labelN.Type.Types.Reverse())
            {
                context.OpStack.PopType(type);
            }
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
    }
    
    //0x0F
    public class InstReturn : InstructionBase
    {
        public override OpCode OpCode => OpCode.Return;

        // @Spec 3.3.8.9. return
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Return != null,
                ()=>$"Instruction return was invalid. Return not set in context");
            var type = context.Return!;
            context.OpStack.ValidateStack(type, false);
        }
        
        // @Spec 4.4.8.9. return
        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Count >= context.Frame.Arity,
                ()=>$"Instruction return failed. Operand stack underflow");
            //We're managing separate stacks, so we won't need to shift the operands
            // var vals = context.OpStack.PopResults(context.Frame.Type.ResultType);
            var frame = context.PopFrame();
            context.ResumeSequence(frame.ContinuationAddress);
        }

        public static readonly InstReturn Inst = new();
    }
    
    //0x10
    public class InstCall : InstructionBase
    {
        public override OpCode OpCode => OpCode.Call;
        
        public FuncIdx X { get; internal set; }

        /// <summary>
        /// @Spec 3.3.8.10. call
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Funcs.Contains(X),
                ()=>$"Instruction call was invalid. Function {X} was not in the Context.");
            var func = context.Funcs[X];
            var type = context.Types[func.TypeIndex];
            context.OpStack.ValidateStack(type.ParameterTypes, false);
            context.OpStack.Push(type.ResultType);
        }
        
        // @Spec 4.4.8.10. call
        public override void Execute(ExecContext context)
        {
            context.Assert(context.Frame.Module.FuncAddrs.Contains(X),
                ()=>$"Instruction call failed. Function address for {X} was not in the Context.");
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
    }
    
    //0x11
    public class InstCallIndirect  : InstructionBase
    {
        public override OpCode OpCode => OpCode.CallIndirect;
        
        public TypeIdx Y { get; internal set; }
        public TableIdx X { get; internal set; }

        /// <summary>
        /// @Spec 3.3.8.11. call_indirect
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                ()=>$"Instruction call_indirect was invalid. Table {X} was not in the Context.");
            var tableType = context.Tables[X];
            context.Assert(tableType.ElementType == ReferenceType.Funcref,
                ()=>$"Instruction call_indirect was invalid. Table type was not funcref");
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
            context.Assert(context.Frame.Module.TableAddrs.Contains(X),
                ()=>$"Instruction call_indirect failed. Table {X} was not in the Context.");
            //3.
            var ta = context.Frame.Module.TableAddrs[X];
            //4.
            context.Assert(context.Store.Contains(ta),
                ()=>$"Instruction call_indirect failed. TableInstance {ta} was not in the Store.");
            //5.
            var tab = context.Store[ta];
            //6.
            context.Assert(context.Frame.Module.Types.Contains(Y),
                () => $"Instruction call_indirect failed. Function Type {Y} was not in the Context.");
            //7.
            var ft_expect = context.Frame.Module.Types[Y];
            //8,9.
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
            context.Assert(r.Type == ValType.Funcref,
                ()=>$"Instruction call_indirect failed. Element was not a FuncRef");
            //14. ???
            var a = context.Frame.Module.FuncAddrs[r.FuncIdx];
            //15.
            context.Assert(context.Store.Contains(a),
                ()=>$"Instruction call_indirect failed. Validation of table mutation failed.");
            //16.
            var funcInst = context.Store[a];
            //17.
            var ft_actual = funcInst.Type;
            //18.
            if (!ft_expect.Matches(ft_actual))
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
    }
}