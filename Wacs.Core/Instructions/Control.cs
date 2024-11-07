using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        public override void Validate(IWasmValidationContext context)
        {
            context.SetUnreachable();
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
        public override void Validate(IWasmValidationContext context)
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
        private static Stack<Value> _asideVals = new();
        public override ByteCode Op => OpCode.Block;
        private Block Block { get; set; } = null!;

        public BlockType Type => Block.Type;

        public int Count => 1;

        public int Size => 1 + Block.Size;
        public InstructionSequence GetBlock(int idx) => Block.Instructions;

        // @Spec 3.3.8.3 block
        public override void Validate(IWasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(Block.Type);
                context.Assert(funcType,  $"Invalid BlockType: {Block.Type}");
                
                //Check the parameters [t1*] and discard
                context.OpStack.PopValues(funcType.ParameterTypes);
                
                //ControlStack will push the values back on (Control Frame is our Label)
                context.PushControlFrame(Op, funcType);
                
                //Continue on to instructions in sequence
                context.ValidateBlock(Block);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    $"Instruction block was invalid. BlockType {Block.Type} did not exist in the Context.");
            }
        }

        // @Spec 4.4.8.3. block
        public override void Execute(ExecContext context) => ExecuteInstruction(context, Block, Op);

        public static void ExecuteInstruction(ExecContext context, Block block, ByteCode inst)
        {
            if (block.Length == 0)
                return;
            
            try
            {
                //2.
                var funcType = context.Frame.Module.Types.ResolveBlockType(block.Type);
                //3.
                context.Assert(funcType, $"Invalid BlockType: {block.Type}");
                //4.
                var label = new Label(funcType.ResultType, context.GetPointer(), inst);
                //5.
                context.Assert(context.OpStack.Count >= funcType.ParameterTypes.Length,
                    $"Instruction block failed. Operand Stack underflow.");
                //6. 
                context.Assert(_asideVals.Count == 0,
                    "Shared temporary stack had values left in it.");
                context.OpStack.PopResults(funcType.ParameterTypes, ref _asideVals);
                //7.
                context.EnterBlock(label, block, _asideVals);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                context.Assert(false,
                    $"Instruction block failed. BlockType {block.Type} did not exist in the Context.");
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

        public override string RenderText(ExecContext? context)
        {
            if (context == null || !context.Attributes.Live) return base.RenderText(context);
            return $"{base.RenderText(context)}  ;; label = @{context.Frame.Labels.Count}";
        }
    }

    //0x03
    public class InstLoop : InstructionBase, IBlockInstruction
    {
        private Stack<Value> _asideVals = new();
        public override ByteCode Op => OpCode.Loop;
        private Block Block { get; set; } = null!;

        public BlockType Type => Block.Type;

        public int Count => 1;

        public int Size => 1 + Block.Size;
        public InstructionSequence GetBlock(int idx) => Block.Instructions;

        // @Spec 3.3.8.4. loop
        public override void Validate(IWasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(Block.Type);
                context.Assert(funcType, $"Invalid BlockType: {Block.Type}");
                
                //Check the parameters [t1*] and discard
                context.OpStack.PopValues(funcType.ParameterTypes);
                
                //ControlStack will push the values back on (Control Frame is our Label)
                context.PushControlFrame(Op, funcType);
                
                //Continue on to instructions in sequence
                context.ValidateBlock(Block);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    $"Instruciton loop invalid. BlockType {Block.Type} did not exist in the Context.");
            }
        }

        // @Spec 4.4.8.4. loop
        public override void Execute(ExecContext context)
        {
            try
            {
                //2.
                var funcType = context.Frame.Module.Types.ResolveBlockType(Block.Type);
                //3.
                context.Assert(funcType, $"Invalid BlockType: {Block.Type}");
                //4.
                var label = new Label(funcType.ResultType, context.GetPointer(), OpCode.Loop);
                //5.
                context.Assert( context.OpStack.Count >= funcType.ParameterTypes.Length,
                    $"Instruction loop failed. Operand Stack underflow.");
                //6. 
                context.Assert(_asideVals.Count == 0,
                    "Shared temporary stack had values left in it.");
                context.OpStack.PopResults(funcType.ParameterTypes, ref _asideVals);
                //7.
                context.EnterBlock(label, Block, _asideVals);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                context.Assert( false,
                    $"Instruction loop failed. BlockType {Block.Type} did not exist in the Context.");
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

        public override string RenderText(ExecContext? context)
        {
            if (context == null || !context.Attributes.Live) return base.RenderText(context);
            return $"{base.RenderText(context)}  ;; label = @{context.Frame.Labels.Count}";
        }
    }

    //0x04
    public class InstIf : InstructionBase, IBlockInstruction
    {
        public override ByteCode Op => OpCode.If;
        private Block IfBlock { get; set; } = Block.Empty;
        private Block ElseBlock { get; set; } = Block.Empty;

        public BlockType Type => IfBlock.Type;

        public int Count => ElseBlock.Length == 0 ? 1 : 2;

        public int Size => 1 + IfBlock.Size + ElseBlock.Size;
        public InstructionSequence GetBlock(int idx) => idx == 0 ? IfBlock.Instructions : ElseBlock.Instructions;

        // @Spec 3.3.8.5 if
        public override void Validate(IWasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(IfBlock.Type);
                context.Assert(funcType,  $"Invalid BlockType: {IfBlock.Type}");

                //Pop the predicate
                context.OpStack.PopI32();
                
                //Check the parameters [t1*] and discard
                context.OpStack.PopValues(funcType.ParameterTypes);
                
                //ControlStack will push the values back on (Control Frame is our Label)
                context.PushControlFrame(Op, funcType);

                //Continue on to instructions in sequence
                // *any (end) contained within will pop the control frame and check values
                // *any (else) contained within will pop and repush the control frame
                context.ValidateBlock(IfBlock);
                
                if (ElseBlock.Length == 0)
                    return;
                
                //Continue on to instructions in sequence
                // *(end) contained within will pop the control frame and check values
                context.ValidateBlock(ElseBlock, 1);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    $"Instruciton loop invalid. BlockType {IfBlock.Type} did not exist in the Context.");
            }
        }

        // @Spec 4.4.8.5. if
        public override void Execute(ExecContext context)
        {
            int c = context.OpStack.PopI32();
            if (c != 0)
            {
                InstBlock.ExecuteInstruction(context, IfBlock, OpCode.If);
            }
            else
            {
                InstBlock.ExecuteInstruction(context, ElseBlock, OpCode.Else);
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
                if (ElseBlock.Length == 0)
                    throw new FormatException($"Explicit Else block contained no instructions.");
            }
            else
            {
                throw new FormatException($"If block did not terminate correctly.");
            }

            return this;
        }
    }

    //0x05
    public class InstElse : InstEnd
    {
        public new static readonly InstElse Inst = new();
        public override ByteCode Op => OpCode.Else;

        public override void Validate(IWasmValidationContext context)
        {
            var frame = context.PopControlFrame();
            context.Assert(frame.Opcode == OpCode.If,
                "Else terminated a non-If block");
            context.PushControlFrame(OpCode.Else, frame.Types);
        }
    }

    //0x0B
    public class InstEnd : InstructionBase
    {
        public static readonly InstEnd Inst = new();
        public override ByteCode Op => OpCode.End;

        public override void Validate(IWasmValidationContext context)
        {
            var frame = context.PopControlFrame();
            context.OpStack.ReturnResults(frame.EndTypes);
        }

        public override void Execute(ExecContext context)
        {
            var label = context.Frame.Label;
            switch (label.Instruction.x00)
            {
                case OpCode.Block:
                case OpCode.If:
                case OpCode.Else:
                case OpCode.Loop:
                    context.ExitBlock();
                    break;
                case OpCode.Expr:
                case OpCode.Call:
                    context.FunctionReturn();
                    break;
                default:
                    //Do nothing
                    break;
            }
        }

        public override string RenderText(ExecContext? context)
        {
            if (context == null)
            {
                return $"{base.RenderText(context)}";
            }
            var label = context.Frame.Label;
            switch (label.Instruction.x00)
            {
                case OpCode.Block:
                case OpCode.If:
                case OpCode.Else:
                    return $"{base.RenderText(context)} (;B/@{context.Frame.Labels.Count-1};)";
                case OpCode.Loop:
                    return $"{base.RenderText(context)} (;L/@{context.Frame.Labels.Count-1};)";
                case OpCode.Expr:
                case OpCode.Call:
                    var funcAddr = context.Frame.Module.FuncAddrs[context.Frame.Index];
                    var func = context.Store[funcAddr];
                    var funcName = func.Id;
                    StringBuilder sb = new();
                    if (context.Attributes.Live)
                    {
                        sb.Append(" ");
                        Stack<Value> values = new Stack<Value>();
                        context.OpStack.PopResults(func.Type.ResultType, ref values);
                        sb.Append("[");
                        while (values.Count > 0)
                        {
                            sb.Append(values.Peek().ToString());
                            if (values.Count > 1)
                                sb.Append(" ");
                            context.OpStack.PushValue(values.Pop());
                        }
                        sb.Append("]");
                    }
                    return $"{base.RenderText(context)} (;f/@{context.Frame.Labels.Count-1} <- {funcName}{sb};)";
                default:
                    return $"{base.RenderText(context)}";
            }
        }
    }

    //0x0C
    public class InstBranch : InstructionBase, IBranchInstruction
    {
        private static Stack<Value> _asideVals = new();
        public override ByteCode Op => OpCode.Br;

        public LabelIdx L { get; internal set; }

        // @Spec 3.3.8.6. br l
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                $"Instruction br invalid. Could not branch to label {L}");

            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            
            //Validate results, but leave them on the stack
            context.OpStack.PopValues(nthFrame.LabelTypes);
            
            context.SetUnreachable();
        }

        // @Spec 4.4.8.6. br l
        public override void Execute(ExecContext context) => ExecuteInstruction(context, L);

        public static void ExecuteInstruction(ExecContext context, LabelIdx labelIndex)
        {
            //1.
            context.Assert( context.Frame.Labels.Count > (int)labelIndex.Value,
                $"Instruction br failed. Context did not contain Label {labelIndex}");
            //2.
            var labels = context.Frame.Labels;
            for (uint i = 0, l = labelIndex.Value; i < l; ++i)
            {
                var top = labels.Pop();
                context.ResumeSequence(top.ContinuationAddress);
            }
            
            var label = labels.Peek();
            //3,4.
            context.Assert( context.OpStack.Count >= label.Arity,
                $"Instruction br failed. Not enough values on the stack.");
            //5.
            context.Assert(_asideVals.Count == 0,
                "Shared temporary stack had values left in it.");
            context.OpStack.PopResults(label.Type, ref _asideVals);
            //6.
            while (context.OpStack.Count > label.StackHeight)
            {
                context.OpStack.PopAny();
            }
            //We did this in part 2 to avoid stack drilling.
            // for (uint i = 0, l = labelIndex.Value; i < l; ++i)
            //     labels.Pop();
            context.ResetStack(label);
            //7.
            context.OpStack.Push(_asideVals);
            //8. 
            if (label.Instruction.x00 == OpCode.Loop)
            {
                //loop targets the loop head
                context.RewindSequence();
            }
            else
            {
                //let InstEnd handle the continuation address and popping of the label
                context.FastForwardSequence();
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

        public override string RenderText(ExecContext? context)
        {
            if (context == null) return $"{base.RenderText(context)} {L.Value}";
            int depth = context.Frame.Labels.Count - 1;
            return $"{base.RenderText(context)} {L.Value} (;@{depth - L.Value};)";
        }
    }

    //0x0D
    public class InstBranchIf : InstructionBase, IBranchInstruction
    {
        public override ByteCode Op => OpCode.BrIf;

        public LabelIdx L { get; internal set; }

        // @Spec 3.3.8.7. br_if
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                $"Instruction br_if invalid. Could not branch to label {L}");
            
            //Pop the predicate
            context.OpStack.PopI32();
            
            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            //Pop values like we branch
            context.OpStack.PopValues(nthFrame.LabelTypes);
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);
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

        public override string RenderText(ExecContext? context)
        {
            if (context == null) return $"{base.RenderText(context)} {L.Value}";
            
            int depth = context.Frame.Labels.Count - 1;
            string taken = "";
            if (context.Attributes.Live)
            {
                taken = (int)context.OpStack.Peek() != 0 ? "-> " : "X: ";
            }
            return $"{base.RenderText(context)} {L.Value} (;{taken}@{depth - L.Value};)";
        }
    }

    //0x0E
    public class InstBranchTable : InstructionBase, IBranchInstruction
    {
        public override ByteCode Op => OpCode.BrTable;

        private LabelIdx[] Ls { get; set; } = null!;
        private LabelIdx Ln { get; set; } //Default m

        // @Spec 3.3.8.8. br_table
        public override void Validate(IWasmValidationContext context)
        {
            //Pop the switch
            context.OpStack.PopI32();
            context.Assert(context.ContainsLabel(Ln.Value),
                $"Instruction br_table invalid. Context did not contain Label {Ln}");
            
            var mthFrame = context.ControlStack.PeekAt((int)Ln.Value);
            var arity = mthFrame.LabelTypes.Arity;
            
            foreach (var lidx in Ls)
            {
                context.Assert(context.ContainsLabel(lidx.Value),
                    $"Instruction br_table invalid. Context did not contain Label {lidx}");
                
                var nthFrame = context.ControlStack.PeekAt((int)lidx.Value);
                context.Assert(nthFrame.LabelTypes.Arity == arity,
                    $"Instruction br_table invalid. Label {lidx} had different arity {nthFrame.LabelTypes.Arity} =/= {arity}");

                var vals = context.OpStack.PopValues(nthFrame.LabelTypes);
                context.OpStack.PushValues(vals);
            }

            context.OpStack.PopValues(mthFrame.LabelTypes);
            context.SetUnreachable();
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
            if (i >= 0 && i < Ls.Length)
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

        public override string RenderText(ExecContext? context)
        {
            if (context==null)
                return $"{base.RenderText(context)} {string.Join(" ", Ls.Select(idx => idx.Value).Select(v => $"{v}"))} {Ln.Value}";
            int depth = context.Frame.Labels.Count-1;

            int index = -2;
            if (context.Attributes.Live)
            {
                int c = context.OpStack.Peek();
                if (c < Ls.Length)
                {
                    index = c;
                }
                else
                {
                    index = -1;
                }
            }

            StringBuilder sb = new();
            int i = 0;
            foreach (var idx in Ls)
            {
                sb.Append(" ");
                sb.Append(i == index
                    ? $"{idx.Value} (;-> @{depth - idx.Value};)"
                    : $"{idx.Value} (;@{depth - idx.Value};)");
                i += 1;
            }
            sb.Append(index == -1
                ? $"{(i > 0 ? " " : "")}{Ln.Value} (;-> @{depth - Ln.Value};)"
                : $"{(i > 0 ? " " : "")}{Ln.Value} (;@{depth - Ln.Value};)");

            return
                $"{base.RenderText(context)} {sb}";
        }
    }

    //0x0F
    public class InstReturn : InstructionBase
    {
        public static readonly InstReturn Inst = new();
        public override ByteCode Op => OpCode.Return;

        // @Spec 3.3.8.9. return
        public override void Validate(IWasmValidationContext context)
        {
            //keep the results for the block or function to validate
            var vals = context.OpStack.PopValues(context.ReturnType);
            context.OpStack.PushValues(vals);
            context.SetUnreachable();
        }

        // @Spec 4.4.8.9. return
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Count >= context.Frame.Arity,
                $"Instruction return failed. Operand stack underflow");
            //We're managing separate stacks, so we won't need to shift the operands
            // var vals = context.OpStack.PopResults(context.Frame.Type.ResultType);
            var frame = context.PopFrame();
            context.ResumeSequence(frame.ContinuationAddress);
        }
    }

    //0x10
    public class InstCall : InstructionBase, ICallInstruction
    {
        public override ByteCode Op => OpCode.Call;

        public FuncIdx X { get; set; }

        public bool IsBound(ExecContext context)
        {
            var a = context.Frame.Module.FuncAddrs[X];
            var func = context.Store[a];
            return func is HostFunction;
        }

        /// <summary>
        /// @Spec 3.3.8.10. call
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Funcs.Contains(X),
                $"Instruction call was invalid. Function {X} was not in the Context.");
            var func = context.Funcs[X];
            var type = context.Types[func.TypeIndex];
            context.OpStack.PopValues(type.ParameterTypes);
            context.OpStack.PushResult(type.ResultType);
        }

        // @Spec 4.4.8.10. call
        public override void Execute(ExecContext context)
        {
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call failed. Function address for {X} was not in the Context.");
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

        public override string RenderText(ExecContext? context)
        {
            if (context != null)
            {
                var a = context.Frame.Module.FuncAddrs[X];
                var func = context.Store[a];
                if (!string.IsNullOrEmpty(func.Id))
                {
                    StringBuilder sb = new();
                    if (context.Attributes.Live)
                    {
                        sb.Append(" ");
                        Stack<Value> values = new Stack<Value>();
                        context.OpStack.PopResults(func.Type.ParameterTypes, ref values);
                        sb.Append("[");
                        while (values.Count > 0)
                        {
                            sb.Append(values.Peek().ToString());
                            if (values.Count > 1)
                                sb.Append(" ");
                            context.OpStack.PushValue(values.Pop());
                        }
                        sb.Append("]");
                    }
                    
                    return $"{base.RenderText(context)} {X.Value} (; -> {func.Id}{sb};)";
                }
            }
            return $"{base.RenderText(context)} {X.Value}";
        }
    }

    //0x11
    public class InstCallIndirect : InstructionBase, ICallInstruction
    {
        public override ByteCode Op => OpCode.CallIndirect;

        private TypeIdx Y { get; set; }
        private TableIdx X { get; set; }

        public bool IsBound(ExecContext context)
        {
            try
            {
                var ta = context.Frame.Module.TableAddrs[X];
                var tab = context.Store[ta];
                int i = context.OpStack.Peek();
                if (i >= tab.Elements.Count)
                    throw new TrapException($"Instruction call_indirect could not find element {i}");
                var r = tab.Elements[i];
                if (r.IsNullRef)
                    throw new TrapException($"Instruction call_indirect NullReference.");
                var a = (FuncAddr)r;
                var func = context.Store[a];
                return func is HostFunction;
            }
            catch (TrapException)
            {
                return false;
            }
        }

        /// <summary>
        /// @Spec 3.3.8.11. call_indirect
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                $"Instruction call_indirect was invalid. Table {X} was not in the Context.");
            var tableType = context.Tables[X];
            context.Assert(tableType.ElementType == ReferenceType.Funcref,
                $"Instruction call_indirect was invalid. Table type was not funcref");
            context.Assert(context.Types.Contains(Y),
                $"Instruction call_indirect was invalid. Function type {Y} was not in the Context.");
            var funcType = context.Types[Y];

            context.OpStack.PopI32();
            context.OpStack.PopValues(funcType.ParameterTypes);
            context.OpStack.PushResult(funcType.ResultType);
        }

        // @Spec 4.4.8.11. call_indirect
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.TableAddrs.Contains(X),
                $"Instruction call_indirect failed. Table {X} was not in the Context.");
            //3.
            var ta = context.Frame.Module.TableAddrs[X];
            //4.
            context.Assert( context.Store.Contains(ta),
                $"Instruction call_indirect failed. TableInstance {ta} was not in the Store.");
            //5.
            var tab = context.Store[ta];
            //6.
            context.Assert( context.Frame.Module.Types.Contains(Y),
                $"Instruction call_indirect failed. Function Type {Y} was not in the Context.");
            //7.
            var ftExpect = context.Frame.Module.Types[Y];
            //8.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            uint i = context.OpStack.PopI32();
            //10.
            if (i >= tab.Elements.Count)
                throw new TrapException($"Instruction call_indirect could not find element {i}");
            //11.
            var r = tab.Elements[(int)i];
            //12.
            if (r.IsNullRef)
                throw new TrapException($"Instruction call_indirect NullReference.");
            //13.
            context.Assert( r.Type == ValType.Funcref,
                $"Instruction call_indirect failed. Element was not a FuncRef");
            //14.
            var a = (FuncAddr)r;
            //15.
            context.Assert( context.Store.Contains(a),
                $"Instruction call_indirect failed. Validation of table mutation failed.");
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

        public override string RenderText(ExecContext? context)
        {
            if (context != null && context.Attributes.Live)
            {
                try
                {
                    var ta = context.Frame.Module.TableAddrs[X];
                    var tab = context.Store[ta];
                    int i = context.OpStack.Peek();
                    if (i >= tab.Elements.Count)
                        throw new TrapException($"Instruction call_indirect could not find element {i}");
                    var r = tab.Elements[i];
                    if (r.IsNullRef)
                        throw new TrapException($"Instruction call_indirect NullReference.");
                    var a = (FuncAddr)r;
                    var func = context.Store[a];


                    if (!string.IsNullOrEmpty(func.Id))
                    {
                        StringBuilder sb = new();
                        if (context.Attributes.Live)
                        {
                            sb.Append(" ");
                            Stack<Value> values = new Stack<Value>();
                            context.OpStack.PopResults(func.Type.ParameterTypes, ref values);
                            sb.Append("[");
                            while (values.Count > 0)
                            {
                                sb.Append(values.Peek().ToString());
                                if (values.Count > 1)
                                    sb.Append(" ");
                                context.OpStack.PushValue(values.Pop());
                            }

                            sb.Append("]");
                        }

                        return $"{base.RenderText(context)} {X.Value} (; -> {func.Id}{sb};)";
                    }
                }
                catch (TrapException)
                {
                    return $"{base.RenderText(context)}{(X.Value == 0 ? "" : $" {X.Value}")} (type {Y.Value})";
                }
            }
            
            return $"{base.RenderText(context)}{(X.Value == 0 ? "" : $" {X.Value}")} (type {Y.Value})";
        }
    }
}