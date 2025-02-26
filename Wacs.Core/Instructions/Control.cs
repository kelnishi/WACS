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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;
using InstructionPointer = System.Int32;

// @Spec 2.4.8. Control Instructions
// @Spec 5.4.1 Control Instructions
namespace Wacs.Core.Instructions
{
    //0x00
    public sealed class InstUnreachable : InstructionBase
    {
        public static readonly InstUnreachable Inst = new();
        public override ByteCode Op => ByteCode.Unreachable;

        // @Spec 3.3.8.2 unreachable
        public override void Validate(IWasmValidationContext context)
        {
            context.SetUnreachable();
        }

        public override InstructionBase Link(ExecContext context, InstructionPointer pointer)
        {
            context.LinkUnreachable = true;
            return this;
        }

        // @Spec 4.4.8.2. unreachable
        public override void Execute(ExecContext context) =>
            throw new TrapException("unreachable");
    }

    //0x01
    public sealed class InstNop : InstructionBase
    {
        public static readonly InstNop Inst = new();
        public override ByteCode Op => ByteCode.Nop;

        // @Spec 3.3.8.1. nop
        public override void Validate(IWasmValidationContext context)
        {
        }

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            Nop = true;
            return base.Link(context, pointer);
        }

        // @Spec 4.4.8.1. nop
        public override void Execute(ExecContext context) {}
    }

    //0x02
    public class InstBlock : BlockTarget, IBlockInstruction
    {
        private Block Block;
        public override ByteCode Op => ByteCode.Block;

        public ValType BlockType => Block.BlockType;

        public int Count => 1;

        public int BlockSize => 1 + Block.Size;
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
                context.PushControlFrame(ByteCode.Block, funcType);
                
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
            // context.Frame.PushLabel(this);
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

        // public override string RenderText(ExecContext? context)
        // {
        //     if (context == null || !context.Attributes.Live) return base.RenderText(context);
        //     return $"{base.RenderText(context)}  ;; label = @{context.Frame.TopLabel.LabelHeight}";
        // }
    }

    //0x03
    public class InstLoop : BlockTarget, IBlockInstruction
    {
        private Block Block = null!;
        public override ByteCode Op => ByteCode.Loop;

        public ValType BlockType => Block.BlockType;

        public int Count => 1;

        public int BlockSize => 1 + Block.Size;
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
                context.PushControlFrame(ByteCode.Loop, funcType);
                
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
            // context.Frame.PushLabel(this);
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

        // public override string RenderText(ExecContext? context)
        // {
        //     if (context == null || !context.Attributes.Live) return base.RenderText(context);
        //     return $"{base.RenderText(context)}  ;; label = @{context.Frame.TopLabel.LabelHeight}";
        // }
    }

    //0x04
    public class InstIf : BlockTarget, IBlockInstruction, IIfInstruction
    {
        
        private Block ElseBlock = Block.Empty;

        private Block IfBlock = Block.Empty;

        public override ByteCode Op => ByteCode.If;

        //Consume the predicate
        public override int StackDiff => -1;


        public ValType BlockType => IfBlock.BlockType;

        public int Count => ElseBlock.Length == 0 ? 1 : 2;

        public int BlockSize => 1 + IfBlock.Size + ElseBlock.Size;
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
                context.PushControlFrame(ByteCode.If, ifType);

                //Continue on to instructions in sequence
                // *any (end) contained within will pop the control frame and check values
                // *any (else) contained within will pop and repush the control frame
                context.ValidateBlock(IfBlock);
                
                var elseType = context.Types.ResolveBlockType(ElseBlock.BlockType);
                if (!ifType.Equivalent(elseType))
                    throw new ValidationException($"If block returned type {ifType}" +
                                                  $" without matching else ({elseType}) block");
                
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
            // context.Frame.PushLabel(this);
            int c = context.OpStack.PopI32();
            if (c == 0)
            {
                context.InstructionPointer = Else - 1;
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
            return this;
        }
    }

    //0x05
    public class InstElse : BlockTarget
    {
        public override ByteCode Op => ByteCode.Else;

        public override void Validate(IWasmValidationContext context)
        {
            var frame = context.PopControlFrame();
            context.Assert(frame.Opcode == OpCode.If, "Else terminated a non-If block");
            context.PushControlFrame(ByteCode.Else, frame.Types);
        }

        public override InstructionBase Link(ExecContext context, InstructionPointer pointer)
        {
            var target = context.PeekLabel();
            target.Suboridinate = this;
            
            if (target is not IIfInstruction)
                throw new InstantiationException($"Else block instruction mismatched to {target}");

            EnclosingBlock = target.EnclosingBlock;
            Label = target.Label;
            target.Else = pointer + 1;

            //Reset and re-consume the predicate
            context.LinkOpStackHeight = target.Label.StackHeight;
            return this;
        }

        public override void Execute(ExecContext context)
        {
            //Just jump out of the If block
            // context.EnterSequence(End);
            context.InstructionPointer = End - 1;
        }
    }

    //0x0B
    public class InstEnd : InstructionBase
    {
        public bool FunctionEnd;

        // public static readonly InstEnd Inst = new();
        public override ByteCode Op => ByteCode.End;

        public override void Validate(IWasmValidationContext context)
        {
            var frame = context.PopControlFrame();
            context.OpStack.ReturnResults(frame.EndTypes);
        }

        public override InstructionBase Link(ExecContext context, InstructionPointer pointer)
        {
            var target = context.PopLabel();
            target.End = pointer;
            if (target.Suboridinate != null)
                target.Suboridinate.End = pointer;

            if (target is IIfInstruction && target.Else < 0)
                target.Else = pointer;

            context.LinkUnreachable = false;
            context.LinkOpStackHeight = target.Label.StackHeight;
            context.LinkOpStackHeight -= target.Label.Parameters;
            context.LinkOpStackHeight += target.Label.Results;

            if (!FunctionEnd)
            {
                Nop = true;
            }
            
            return this;
        }

        //Skipped unless FunctionEnd is true
        public override void Execute(ExecContext context)
        {
            context.FunctionReturn();
        }

        // public override string RenderText(ExecContext? context)
        // {
        //     if (context == null)
        //     {
        //         return $"{base.RenderText(context)}";
        //     }
        //     var label = context.Frame.Label;
        //     switch (label.Instruction.x00)
        //     {
        //         case OpCode.Block:
        //         case OpCode.If:
        //         case OpCode.Else:
        //             return $"{base.RenderText(context)} (;B/@{context.Frame.TopLabel.LabelHeight-1};)";
        //         case OpCode.Loop:
        //             return $"{base.RenderText(context)} (;L/@{context.Frame.TopLabel.LabelHeight-1};)";
        //         case OpCode.Func:
        //         case OpCode.Call:
        //             var funcAddr = context.Frame.Module.FuncAddrs[context.Frame.Index];
        //             var func = context.Store[funcAddr];
        //             var funcName = func.Id;
        //             StringBuilder sb = new();
        //             if (context.Attributes.Live)
        //             {
        //                 sb.Append(" ");
        //                 var values = new Stack<Value>();
        //                 context.OpStack.PopResults(func.Type.ResultType, ref values);
        //                 sb.Append("[");
        //                 while (values.Count > 0)
        //                 {
        //                     sb.Append(values.Peek().ToString());
        //                     if (values.Count > 1)
        //                         sb.Append(" ");
        //                     context.OpStack.PushValue(values.Pop());
        //                 }
        //                 sb.Append("]");
        //             }
        //             return $"{base.RenderText(context)} (;f/@{context.Frame.TopLabel.LabelHeight-1} <- {funcName}{sb};)";
        //         default:
        //             return $"{base.RenderText(context)}";
        //     }
        // }
    }

    //0x0C
    public sealed class InstBranch : InstructionBase, IBranchInstruction
    {
        private static Stack<Value> _asideVals = new();

        private LabelIdx L;
        private BlockTarget? LinkedLabel;

        public override ByteCode Op => ByteCode.Br;

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

        public static BlockTarget PrecomputeStack(ExecContext context, LabelIdx labelIndex)
        {
            var label = context.PeekLabel();
            if (labelIndex.Value > 0)
            {
                for (int l = 0; l < labelIndex.Value; l++) 
                    label = label.EnclosingBlock;
            }
            return label;
        }

        public static void SetStackHeight(ExecContext context, BlockTarget label)
        {
            context.LinkOpStackHeight = label.Label.StackHeight + label.Label.Arity;
        }

        public override InstructionBase Link(ExecContext context, InstructionPointer pointer)
        {
            LinkedLabel = PrecomputeStack(context, L);
            SetStackHeight(context, LinkedLabel);
            context.LinkUnreachable = true;
            return this;
        }

        // @Spec 4.4.8.6. br l
        public override void Execute(ExecContext context)
        {
            ExecuteInstruction(context, LinkedLabel);
        }

        public static void ExecuteInstruction(ExecContext context, BlockTarget? target)
        {
            var label = target?.Label switch
            {
                null => context.Frame.ReturnLabel,
                { Instruction: { x00: OpCode.Func} } => context.Frame.ReturnLabel,
                var l => l
            };
            
            context.Assert( context.OpStack.Count >= label.Arity,
                $"Instruction br failed. Not enough values on the stack.");
            context.Assert(_asideVals.Count == 0,
                "Shared temporary stack had values left in it.");

            //Only reset the stack if there blocks containing extra values.
            // An ideal solution would be to slice the OpStack array, but we don't have access.
            if (context.OpStack.Count > context.Frame.StackHeight + label.StackHeight + label.Arity)
            {
                //TODO Move the elements in OpStack's registers array.
                context.OpStack.PopResults(label.Arity, ref _asideVals);
                context.ResetStack(label);
                context.OpStack.PushResults(_asideVals);
            }

            switch (label.Instruction.x00)
            {
                //8. 
                case OpCode.Func:
                    context.InstructionPointer = label.ContinuationAddress;
                    break;
                case OpCode.Loop:
                    //loop targets the loop head
                    // context.InstructionPointer = context.Frame.TopLabel.Head;
                    context.InstructionPointer = target.Head; 
                    break;
                default:
                    //otherwise, go to the end
                    // context.InstructionPointer = context.Frame.TopLabel.End - 1;
                    context.InstructionPointer = target.End - 1; 
                    break;
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

        // public override string RenderText(ExecContext? context)
        // {
        //     if (context == null) return $"{base.RenderText(context)} {L.Value}";
        //     int depth = context.Frame.TopLabel.LabelHeight - 1;
        //     return $"{base.RenderText(context)} {L.Value} (;@{depth - L.Value};)";
        // }
    }

    //0x0D
    public sealed class InstBranchIf : InstructionBase, IBranchInstruction, IComplexLinkBehavior, INodeConsumer<int>
    {
        public LabelIdx L;
        private BlockTarget? LinkedLabel;

        public override ByteCode Op => ByteCode.BrIf;
        public override int StackDiff => -1;

        public Action<ExecContext, int> GetFunc => BranchIf;

        // @Spec 3.3.8.7. br_if
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                "Instruction br_if invalid. Could not branch to label {0}",L);
            
            //Pop the predicate
            context.OpStack.PopI32();   // -1
            
            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            
            //Pop values like we branch
            context.OpStack.DiscardValues(nthFrame.LabelTypes); // -(N+1)
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);    // -1
        }

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            LinkedLabel = InstBranch.PrecomputeStack(context, L);
            return base.Link(context, pointer);
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
                InstBranch.ExecuteInstruction(context, LinkedLabel);
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

        // public override string RenderText(ExecContext? context)
        // {
        //     if (context == null) return $"{base.RenderText(context)} {L.Value}";
        //     
        //     int depth = context.Frame.TopLabel.LabelHeight - 1;
        //     string taken = "";
        //     if (context.Attributes.Live)
        //     {
        //         taken = context.OpStack.Peek().Data.Int32 != 0 ? "-> " : "X: ";
        //     }
        //     return $"{base.RenderText(context)} {L.Value} (;{taken}@{depth - L.Value};)";
        // }
    }

    //0x0E
    public sealed class InstBranchTable : InstructionBase, IBranchInstruction, IComplexLinkBehavior, INodeConsumer<int>
    {
        private BlockTarget? LinkedLabeln;
        private BlockTarget?[] LinkedLabels;
        private LabelIdx Ln; //Default m

        private LabelIdx[] Ls = null!;

        public override ByteCode Op => ByteCode.BrTable;

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

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            LinkedLabeln = InstBranch.PrecomputeStack(context, Ln);
            int stack = context.LinkOpStackHeight;
            InstBranch.SetStackHeight(context, LinkedLabeln);
            StackDiff = context.LinkOpStackHeight - stack;
            
            LinkedLabels = Ls.Select(l => InstBranch.PrecomputeStack(context, l)).ToArray();
            context.LinkUnreachable = true;
            return this;
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
                var label = LinkedLabels[i];
                InstBranch.ExecuteInstruction(context, label);
            }
            //4.
            else
            {
                InstBranch.ExecuteInstruction(context, LinkedLabeln);
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

        // public override string RenderText(ExecContext? context)
        // {
        //     if (context==null)
        //         return $"{base.RenderText(context)} {string.Join(" ", Ls.Select(idx => idx.Value).Select(v => $"{v}"))} {Ln.Value}";
        //     int depth = context.Frame.TopLabel.LabelHeight-1;
        //
        //     int index = -2;
        //     if (context.Attributes.Live)
        //     {
        //         int c = context.OpStack.Peek().Data.Int32;
        //         if (c < Ls.Length)
        //         {
        //             index = c;
        //         }
        //         else
        //         {
        //             index = -1;
        //         }
        //     }
        //
        //     StringBuilder sb = new();
        //     int i = 0;
        //     foreach (var idx in Ls)
        //     {
        //         sb.Append(" ");
        //         sb.Append(i == index
        //             ? $"{idx.Value} (;-> @{depth - idx.Value};)"
        //             : $"{idx.Value} (;@{depth - idx.Value};)");
        //         i += 1;
        //     }
        //     sb.Append(index == -1
        //         ? $"{(i > 0 ? " " : "")}{Ln.Value} (;-> @{depth - Ln.Value};)"
        //         : $"{(i > 0 ? " " : "")}{Ln.Value} (;@{depth - Ln.Value};)");
        //
        //     return
        //         $"{base.RenderText(context)} {sb}";
        // }
    }

    //0x0F
    public sealed class InstReturn : InstructionBase
    {
        public static readonly InstReturn Inst = new();
        public override ByteCode Op => ByteCode.Return;

        // @Spec 3.3.8.9. return
        public override void Validate(IWasmValidationContext context)
        {
            //keep the results for the block or function to validate
            Stack<Value> aside = new();
            context.OpStack.PopValues(context.ReturnType, ref aside);
            context.OpStack.PushValues(aside);
            context.SetUnreachable();
        }

        public override InstructionBase Link(ExecContext context, InstructionPointer pointer)
        {
            context.LinkUnreachable = true;
            return this;
        }

        // @Spec 4.4.8.9. return
        public override void Execute(ExecContext context)
        {
            
            // var address = context.PopFrame();
            // context.ResumeSequence(address);
            context.FunctionReturn();
        }
    }

    //0x10
    public sealed class InstCall : InstructionBase, ICallInstruction
    {
        public FuncIdx X;
        private bool IsHostFunction = false;
        private FunctionInstance? linkedFunctionInstance;
        private HostFunction? linkedHostFunction;

        public InstCall()
        {
            IsAsync = true;
        }

        public override ByteCode Op => ByteCode.Call;

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

        public override InstructionBase Link(ExecContext context, InstructionPointer pointer)
        {
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call failed. Function address for {X} was not in the Context.");
            var a = context.Frame.Module.FuncAddrs[X];
            var inst = context.Store[a];
            switch (inst)
            {
                case FunctionInstance wasmFunc:
                    IsAsync = false;
                    linkedFunctionInstance = wasmFunc;
                    IsHostFunction = false;
                    break;
                case HostFunction hostFunction:
                    IsAsync = hostFunction.IsAsync;
                    linkedHostFunction = hostFunction;
                    IsHostFunction = true;
                    break;
            }

            var funcType = inst.Type;
            int stack = context.LinkOpStackHeight;
            context.LinkOpStackHeight -= funcType.ParameterTypes.Arity;
            context.LinkOpStackHeight += funcType.ResultType.Arity;
            //For recordkeeping
            StackDiff = context.LinkOpStackHeight - stack;
            
            return this;
        }

        // @Spec 4.4.8.10. call
        public override void Execute(ExecContext context)
        {
            if (IsHostFunction)
                linkedHostFunction!.Invoke(context);
            else
                linkedFunctionInstance!.Invoke(context);
        }

        public override async ValueTask ExecuteAsync(ExecContext context)
        {
            if (IsHostFunction)
            {
                if (linkedHostFunction!.IsAsync)
                    await linkedHostFunction!.InvokeAsync(context);
                else
                    linkedHostFunction!.Invoke(context);
            }
            else
                linkedFunctionInstance!.Invoke(context);
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

        public override ByteCode Op => ByteCode.CallIndirect;

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

            var at = tableType.Limits.AddressType;
            context.OpStack.PopType(at.ToValType());                // -1
            context.OpStack.DiscardValues(funcType.ParameterTypes); // -(N+1)
            context.OpStack.PushResult(funcType.ResultType);        // -(N+1)+M
        }

        public override InstructionBase Link(ExecContext context, InstructionPointer pointer)
        {
            context.Assert( context.Frame.Module.TableAddrs.Contains(X),
                $"Instruction call_indirect failed. Table {X} was not in the Context.");
            var ta = context.Frame.Module.TableAddrs[X];
            context.Assert( context.Store.Contains(ta),
                $"Instruction call_indirect failed. TableInstance {ta} was not in the Store.");
            var tab = context.Store[ta];
            context.Assert( context.Frame.Module.Types.Contains(Y),
                $"Instruction call_indirect failed. Function Type {Y} was not in the Context.");
            var ftExpect = context.Frame.Module.Types[Y];
            var funcType = ftExpect.Expansion as FunctionType;
            context.Assert(funcType,
                $"Instruction {Op.GetMnemonic()} failed. Not a function type.");

            int stack = context.LinkOpStackHeight;
            context.LinkOpStackHeight -= 1;
            context.LinkOpStackHeight -= funcType.ParameterTypes.Arity;
            context.LinkOpStackHeight += funcType.ResultType.Arity;
            //For recordkeeping
            StackDiff = context.LinkOpStackHeight - stack;
            
            //TODO Precompute call targets, cache the whole table
            
            return this;
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
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            long i = context.OpStack.PopAddr();
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
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            long i = context.OpStack.PopAddr();
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