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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// @Spec 2.4.8. Control Instructions
// @Spec 5.4.1 Control Instructions
namespace Wacs.Core.Instructions
{
    //0x00
    public sealed class InstUnreachable : InstructionBase
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
    public sealed class InstNop : InstructionBase
    {
        public static readonly InstNop Inst = new();
        public override ByteCode Op => OpCode.Nop;

        // @Spec 3.3.8.1. nop
        public override void Validate(IWasmValidationContext context)
        {
        }

        // @Spec 4.4.8.1. nop
        public override void Execute(ExecContext context) {}
    }

    //0x02
    public class InstBlock : BlockTarget, IBlockInstruction
    {
        private static readonly ByteCode BlockOp = OpCode.Block;
        private Block Block;
        public override ByteCode Op => BlockOp;

        public ValType BlockType => Block.BlockType;

        public int Count => 1;

        public int Size => 1 + Block.Size;
        public Block GetBlock(int idx) => Block;

        // @Spec 3.3.8.3 block
        public override void Validate(IWasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(Block.BlockType);
                context.Assert(funcType,  "Invalid BlockType: {0}",Block.BlockType);
                
                //Check the parameters [t1*] and discard
                context.OpStack.DiscardValues(funcType.ParameterTypes);
                
                //ControlStack will push the values back on (Control Frame is our Label)
                context.PushControlFrame(BlockOp, funcType);
                
                //Continue on to instructions in sequence
                context.ValidateBlock(Block);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    "Instruction block was invalid. BlockType {0} did not exist in the Context.",Block.BlockType);
            }
        }

        // @Spec 4.4.8.3. block
        public override void Execute(ExecContext context)
        {
            context.EnterBlock(this, Block);
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override InstructionBase Parse(BinaryReader reader)
        {
            Block = new Block(
                blockType: ValTypeParser.Parse(reader, parseBlockIndex: true, parseStorageType: false),
                seq: new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction, IsEnd))
            );
            return this;
        }

        public BlockTarget Immediate(ValType blockType, InstructionSequence sequence)
        {
            Block = new Block(
                blockType: blockType,
                seq: sequence
            );
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context == null || !context.Attributes.Live) return base.RenderText(context);
            return $"{base.RenderText(context)}  ;; label = @{context.Frame.LabelCount}";
        }
    }

    //0x03
    public class InstLoop : BlockTarget, IBlockInstruction
    {
        private static readonly ByteCode LoopOp = OpCode.Loop;
        private Block Block = null!;
        public override ByteCode Op => LoopOp;

        public ValType BlockType => Block.BlockType;

        public int Count => 1;

        public int Size => 1 + Block.Size;
        public Block GetBlock(int idx) => Block;

        // @Spec 3.3.8.4. loop
        public override void Validate(IWasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(Block.BlockType);
                context.Assert(funcType, "Invalid BlockType: {0}",Block.BlockType);
                
                //Check the parameters [t1*] and discard
                context.OpStack.DiscardValues(funcType.ParameterTypes);
                
                //ControlStack will push the values back on (Control Frame is our Label)
                context.PushControlFrame(LoopOp, funcType);
                
                //Continue on to instructions in sequence
                context.ValidateBlock(Block);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    "Instruction loop invalid. BlockType {0} did not exist in the Context.",Block.BlockType);
            }
        }

        // @Spec 4.4.8.4. loop
        public override void Execute(ExecContext context)
        {
            if (Block.Instructions.Count != 0)
                context.EnterBlock(this, Block);
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override InstructionBase Parse(BinaryReader reader)
        {
            Block = new Block(
                blockType: ValTypeParser.Parse(reader, parseBlockIndex: true, parseStorageType: false),
                seq: new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction, IsEnd))
            );
            return this;
        }

        public BlockTarget Immediate(ValType blockType, InstructionSequence sequence)
        {
            Block = new Block(
                blockType: blockType,
                seq: sequence
            );
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context == null || !context.Attributes.Live) return base.RenderText(context);
            return $"{base.RenderText(context)}  ;; label = @{context.Frame.LabelCount}";
        }
    }

    //0x04
    public class InstIf : BlockTarget, IBlockInstruction
    {
        private static readonly ByteCode IfOp = OpCode.If;
        private static readonly ByteCode ElseOp = OpCode.Else;
        private Block ElseBlock = Block.Empty;
        private int ElseCount;
        private Block IfBlock = Block.Empty;
        public override ByteCode Op => IfOp;


        public ValType BlockType => IfBlock.BlockType;

        public int Count => ElseBlock.Length == 0 ? 1 : 2;

        public int Size => 1 + IfBlock.Size + ElseBlock.Size;
        public Block GetBlock(int idx) => idx == 0 ? IfBlock : ElseBlock;

        // @Spec 3.3.8.5 if
        public override void Validate(IWasmValidationContext context)
        {
            try
            {
                var ifType = context.Types.ResolveBlockType(IfBlock.BlockType);
                context.Assert(ifType,  "Invalid BlockType: {0}",IfBlock.BlockType);

                //Pop the predicate
                context.OpStack.PopI32();
                
                //Check the parameters [t1*] and discard
                context.OpStack.DiscardValues(ifType.ParameterTypes);
                
                //ControlStack will push the values back on (Control Frame is our Label)
                context.PushControlFrame(IfOp, ifType);

                //Continue on to instructions in sequence
                // *any (end) contained within will pop the control frame and check values
                // *any (else) contained within will pop and repush the control frame
                context.ValidateBlock(IfBlock);
                
                var elseType = context.Types.ResolveBlockType(ElseBlock.BlockType);
                if (!ifType.Equivalent(elseType))
                    throw new ValidationException($"If block returned type {ifType} without matching else block");
                
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
                    "Instruction loop invalid. BlockType {0} did not exist in the Context.",IfBlock.BlockType);
            }
        }

        // @Spec 4.4.8.5. if
        public override void Execute(ExecContext context)
        {
            int c = context.OpStack.PopI32();
            if (c != 0)
            {
                context.EnterBlock(this, IfBlock);
            }
            else
            {
                if (ElseCount != 0)
                    context.EnterBlock(this, ElseBlock);
            }
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override InstructionBase Parse(BinaryReader reader)
        {
            IfBlock = new Block(
                blockType: ValTypeParser.Parse(reader, parseBlockIndex: true, parseStorageType: false),
                new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction,
                    IsElseOrEnd))
            );

            if (IfBlock.Instructions.EndsWithElse)
            {
                ElseBlock = new Block(
                    blockType: IfBlock.BlockType,
                    seq: new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction,
                        IsEnd))
                );
            }
            else if (!IfBlock.Instructions.HasExplicitEnd)
            {
                throw new FormatException($"If block did not terminate correctly.");
            }
            
            ElseCount = ElseBlock.Instructions.Count;
            return this;
        }

        public BlockTarget Immediate(ValType blockType, InstructionSequence ifSeq, InstructionSequence elseSeq)
        {
            IfBlock = new Block(
                blockType: blockType,
                seq: ifSeq
            );
            ElseBlock = new Block(
                blockType: blockType,
                seq: elseSeq
            );
            ElseCount = ElseBlock.Instructions.Count;
            return this;
        }
    }

    //0x05
    public class InstElse : InstEnd
    {
        public new static readonly InstElse Inst = new();
        private static readonly ByteCode ElseOp = OpCode.Else;
        public override ByteCode Op => ElseOp;

        public override void Validate(IWasmValidationContext context)
        {
            var frame = context.PopControlFrame();
            context.Assert(frame.Opcode == OpCode.If, "Else terminated a non-If block");
            context.PushControlFrame(ElseOp, frame.Types);
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
                case OpCode.Func:
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
                    return $"{base.RenderText(context)} (;B/@{context.Frame.LabelCount-1};)";
                case OpCode.Loop:
                    return $"{base.RenderText(context)} (;L/@{context.Frame.LabelCount-1};)";
                case OpCode.Func:
                case OpCode.Call:
                    var funcAddr = context.Frame.Module.FuncAddrs[context.Frame.Index];
                    var func = context.Store[funcAddr];
                    var funcName = func.Id;
                    StringBuilder sb = new();
                    if (context.Attributes.Live)
                    {
                        sb.Append(" ");
                        var values = new Stack<Value>();
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
                    return $"{base.RenderText(context)} (;f/@{context.Frame.LabelCount-1} <- {funcName}{sb};)";
                default:
                    return $"{base.RenderText(context)}";
            }
        }
    }

    //0x0C
    public sealed class InstBranch : InstructionBase, IBranchInstruction
    {
        private static Stack<Value> _asideVals = new();

        private LabelIdx L;
        public override ByteCode Op => OpCode.Br;

        // @Spec 3.3.8.6. br l
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                "Instruction br invalid. Could not branch to label {0}",L);

            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            
            //Validate results, but leave them on the stack
            context.OpStack.DiscardValues(nthFrame.LabelTypes);

            context.SetUnreachable();
        }

        // @Spec 4.4.8.6. br l
        public override void Execute(ExecContext context)
        {
            ExecuteInstruction(context, L);
        }

        public static void ExecuteInstruction(ExecContext context, LabelIdx labelIndex)
        {
            //1.
            context.Assert( context.Frame.LabelCount > (int)labelIndex.Value,
                $"Instruction br failed. Context did not contain Label {labelIndex}");
            //2.
            if (labelIndex.Value > 0)
            {
                var topAddr = context.Frame.PopLabels((int)(labelIndex.Value - 1));
                context.ResumeSequence(topAddr);
            }

            var label = context.Frame.Label;
            //3,4.
            context.Assert( context.OpStack.Count >= label.Arity,
                $"Instruction br failed. Not enough values on the stack.");
            //5.
            context.Assert(_asideVals.Count == 0,
                "Shared temporary stack had values left in it.");

            //Only reset the stack if there blocks containing extra values.
            // An ideal solution would be to slice the OpStack array, but we don't have access.
            if (context.OpStack.Count > context.Frame.StackHeight + label.StackHeight + label.Arity)
            {
                //TODO Move the elements in OpStack's registers array.
                context.OpStack.PopResults(label.Arity, ref _asideVals);
                //6.
                context.ResetStack(label);
                //We did this in part 2 to avoid stack drilling.
                // for (uint i = 0, l = labelIndex.Value; i < l; ++i)
                //     labels.Pop();
                //7.
                context.OpStack.PushResults(_asideVals);
            }
            
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
        public override InstructionBase Parse(BinaryReader reader)
        {
            L = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context == null) return $"{base.RenderText(context)} {L.Value}";
            int depth = context.Frame.LabelCount - 1;
            return $"{base.RenderText(context)} {L.Value} (;@{depth - L.Value};)";
        }
    }

    //0x0D
    public sealed class InstBranchIf : InstructionBase, IBranchInstruction, INodeConsumer<int>
    {
        public LabelIdx L;
        public override ByteCode Op => OpCode.BrIf;

        public Action<ExecContext, int> GetFunc => BranchIf;

        // @Spec 3.3.8.7. br_if
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                "Instruction br_if invalid. Could not branch to label {0}",L);
            
            //Pop the predicate
            context.OpStack.PopI32();
            
            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            
            //Pop values like we branch
            context.OpStack.DiscardValues(nthFrame.LabelTypes);
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);
        }

        // @Spec 4.4.8.7. br_if
        public override void Execute(ExecContext context)
        {
            int c = context.OpStack.PopI32();
            BranchIf(context, c);
        }

        private void BranchIf(ExecContext context, int c)
        {
            if (c != 0)
            {
                InstBranch.ExecuteInstruction(context, L);
            }
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override InstructionBase Parse(BinaryReader reader)
        {
            L = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context == null) return $"{base.RenderText(context)} {L.Value}";
            
            int depth = context.Frame.LabelCount - 1;
            string taken = "";
            if (context.Attributes.Live)
            {
                taken = context.OpStack.Peek().Data.Int32 != 0 ? "-> " : "X: ";
            }
            return $"{base.RenderText(context)} {L.Value} (;{taken}@{depth - L.Value};)";
        }
    }

    //0x0E
    public sealed class InstBranchTable : InstructionBase, IBranchInstruction, INodeConsumer<int>
    {
        private LabelIdx Ln; //Default m

        private LabelIdx[] Ls = null!;
        public override ByteCode Op => OpCode.BrTable;

        public Action<ExecContext, int> GetFunc => BranchTable;

        // @Spec 3.3.8.8. br_table
        public override void Validate(IWasmValidationContext context)
        {
            //Pop the switch
            context.OpStack.PopI32();
            context.Assert(context.ContainsLabel(Ln.Value),
                "Instruction br_table invalid. Context did not contain Label {0}", Ln);
            
            var mthFrame = context.ControlStack.PeekAt((int)Ln.Value);
            var arity = mthFrame.LabelTypes.Arity;

            Stack<Value> aside = new();
            foreach (var lidx in Ls)
            {
                context.Assert(context.ContainsLabel(lidx.Value),
                    "Instruction br_table invalid. Context did not contain Label {0}", lidx);
                
                var nthFrame = context.ControlStack.PeekAt((int)lidx.Value);
                context.Assert(nthFrame.LabelTypes.Arity == arity,
                    "Instruction br_table invalid. Label {0} had different arity {1} =/= {2}", lidx, nthFrame.LabelTypes.Arity,arity);
                
                context.OpStack.PopValues(nthFrame.LabelTypes, ref aside);
                context.OpStack.PushValues(aside);
            }

            context.OpStack.PopValues(mthFrame.LabelTypes, ref aside);
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
            BranchTable(context, i);
        }


        private void BranchTable(ExecContext context, int i)
        {
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
        public override InstructionBase Parse(BinaryReader reader)
        {
            Ls = reader.ParseVector(ParseLabelIndex);
            Ln = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context==null)
                return $"{base.RenderText(context)} {string.Join(" ", Ls.Select(idx => idx.Value).Select(v => $"{v}"))} {Ln.Value}";
            int depth = context.Frame.LabelCount-1;

            int index = -2;
            if (context.Attributes.Live)
            {
                int c = context.OpStack.Peek().Data.Int32;
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
    public sealed class InstReturn : InstructionBase
    {
        public static readonly InstReturn Inst = new();
        public override ByteCode Op => OpCode.Return;

        // @Spec 3.3.8.9. return
        public override void Validate(IWasmValidationContext context)
        {
            //keep the results for the block or function to validate
            Stack<Value> aside = new();
            context.OpStack.PopValues(context.ReturnType, ref aside);
            context.OpStack.PushValues(aside);
            context.SetUnreachable();
        }

        // @Spec 4.4.8.9. return
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Count >= context.Frame.Arity,
                $"Instruction return failed. Operand stack underflow");
            int resultCount = context.Frame.Type.ResultType.Arity;
            int resultsHeight = context.Frame.StackHeight + resultCount;
            if (resultsHeight < context.OpStack.Count)
                context.OpStack.ShiftResults(resultCount, resultsHeight);
            var address = context.PopFrame();
            context.ResumeSequence(address);
        }
    }

    //0x10
    public sealed class InstCall : InstructionBase, ICallInstruction
    {
        public FuncIdx X;

        public InstCall()
        {
            IsAsync = true;
        }

        public override ByteCode Op => OpCode.Call;

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
                "Instruction call was invalid. Function {0} was not in the Context.",X);
            var func = context.Funcs[X];
            var type = context.Types[func.TypeIndex].Expansion;
            var funcType = type as FunctionType;
            context.Assert(funcType,
                "Instruction call was invalid. Not a FuncType. {0}", type);
            
            context.OpStack.DiscardValues(funcType.ParameterTypes);
            context.OpStack.PushResult(funcType.ResultType);
        }

        // @Spec 4.4.8.10. call
        public override void Execute(ExecContext context)
        {
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call failed. Function address for {X} was not in the Context.");
            var a = context.Frame.Module.FuncAddrs[X];
            context.Invoke(a);
        }

        public override async ValueTask ExecuteAsync(ExecContext context)
        {
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call failed. Function address for {X} was not in the Context.");
            var a = context.Frame.Module.FuncAddrs[X];
            await context.InvokeAsync(a);
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (FuncIdx)reader.ReadLeb128_u32();
            return this;
        }

        public InstructionBase Immediate(FuncIdx value)
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
                        var values = new Stack<Value>();
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
    public sealed class InstCallIndirect : InstructionBase, ICallInstruction
    {
        private TableIdx X;

        private TypeIdx Y;

        public InstCallIndirect()
        {
            IsAsync = true;
        }

        public override ByteCode Op => OpCode.CallIndirect;

        public bool IsBound(ExecContext context)
        {
            try
            {
                var ta = context.Frame.Module.TableAddrs[X];
                var tab = context.Store[ta];
                int i = context.OpStack.Peek().Data.Int32;
                if (i >= tab.Elements.Count)
                    throw new TrapException($"Instruction call_indirect could not find element {i}");
                var r = tab.Elements[i];
                if (r.IsNullRef)
                    throw new TrapException($"Instruction call_indirect NullReference.");
                context.Assert(r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types),
                    $"Instruction call_indirect failed. Element was not a FuncRef");
                var a = r.GetFuncAddr(context.Frame.Module.Types);
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
                "Instruction call_indirect was invalid. Table {0} was not in the Context.",X);
            var tableType = context.Tables[X];
            context.Assert(tableType.ElementType.Matches(ValType.FuncRef, context.Types),
                "Instruction call_indirect was invalid. Table type was not funcref");
            context.Assert(context.Types.Contains(Y),
                "Instruction call_indirect was invalid. Function type {0} was not in the Context.",Y);
            var type = context.Types[Y].Expansion;
            var funcType = type as FunctionType;
            context.Assert(funcType,
                "Instruction call_indirect was invalid. Not a FuncType. {0}", type);

            context.OpStack.PopI32();
            context.OpStack.DiscardValues(funcType.ParameterTypes);
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
            var ftFunc = ftExpect.Expansion as FunctionType;
            context.Assert(ftFunc,
                $"Instruction {Op.GetMnemonic()} failed. Not a function type.");
            //8.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            uint i = context.OpStack.PopU32();
            //10.
            if (i >= tab.Elements.Count)
                throw new TrapException($"Instruction call_indirect could not find element {i}");
            //11.
            var r = tab.Elements[(int)i];
            //12.
            if (r.IsNullRef)
                throw new TrapException($"Instruction call_indirect NullReference.");
            //13.
            context.Assert(r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types),
                $"Instruction call_indirect failed. Element was not a FuncRef");
            //14
            var a = r.GetFuncAddr(context.Frame.Module.Types);
            //15.
            context.Assert( context.Store.Contains(a),
                $"Instruction call_indirect failed. Validation of table mutation failed.");
            //16.
            var funcInst = context.Store[a];
            //17.
            
            //18.
            //Check wasmfuncs, not hostfuncs
            if (funcInst is FunctionInstance ftAct)
                if (!ftAct.DefType.Matches(ftExpect, context.Frame.Module.Types))
                    throw new TrapException($"Instruction {Op.GetMnemonic()} failed. RecursiveType differed.");
            if (!funcInst.Type.Matches(ftExpect.Unroll.Body, context.Frame.Module.Types))
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Expected FunctionType differed.");
            //19.
            context.Invoke(a);
        }

        public override async ValueTask ExecuteAsync(ExecContext context)
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
            var ftFunc = ftExpect.Expansion as FunctionType;
            context.Assert(ftFunc,
                $"Instruction {Op.GetMnemonic()} failed. Not a function type.");
            //8.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            uint i = context.OpStack.PopU32();
            //10.
            if (i >= tab.Elements.Count)
                throw new TrapException($"Instruction call_indirect could not find element {i}");
            //11.
            var r = tab.Elements[(int)i];
            //12.
            if (r.IsNullRef)
                throw new TrapException($"Instruction call_indirect NullReference.");
            //13.
            context.Assert(r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types),
                $"Instruction call_indirect failed. Element was not a FuncRef");
            //14.
            var a = r.GetFuncAddr(context.Frame.Module.Types);
            //15.
            context.Assert( context.Store.Contains(a),
                $"Instruction call_indirect failed. Validation of table mutation failed.");
            //16.
            var funcInst = context.Store[a];
            //17.

            //18.
            //Check wasmfuncs, not hostfuncs
            if (funcInst is FunctionInstance ftAct)
                if (!ftAct.DefType.Matches(ftExpect, context.Frame.Module.Types))
                    throw new TrapException($"Instruction {Op.GetMnemonic()} failed. RecursiveType differed.");
            if (!funcInst.Type.Matches(ftExpect.Unroll.Body, context.Frame.Module.Types))
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Expected FunctionType differed.");
            //19.
            await context.InvokeAsync(a);
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override InstructionBase Parse(BinaryReader reader)
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
                    if (!r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types))
                        throw new TrapException($"Instruction call_indirect failed. Element was not a FuncRef");
                    var a = r.GetFuncAddr(context.Frame.Module.Types);
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