// Copyright 2025 Kelvin Nishikawa
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
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions
{
    public class InstTryTable : BlockTarget, IBlockInstruction, IExnHandler
    {
        private static readonly ByteCode TryTableOp = OpCode.TryTable;
        private Block Block;
        public CatchType[] Catches;
        
        public override ByteCode Op => TryTableOp;
        public ValType BlockType => Block.BlockType;
        public int Count => 1;
        public int BlockSize => 1 + Block.Size;
        public Block GetBlock(int idx) => Block;
        
        private static readonly ByteCode CatchOp = WacsCode.Catch;
        
        public override void Validate(IWasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(Block.BlockType);
                context.Assert(funcType,  "Invalid BlockType: {0}",Block.BlockType);
                
                //Check the parameters [t1*] and discard
                context.OpStack.DiscardValues(funcType.ParameterTypes);
                
                //Exn Handlers
                context.ValidateCatches(Catches);
                foreach (var handler in Catches)
                {
                    context.Assert(context.ContainsLabel(handler.L.Value),
                        "Catch Label {0} does not exist in the context.", handler.L);
                    var labelFrame = context.ControlStack.PeekAt((int)handler.L.Value);
                    var labelType = new FunctionType(ResultType.Empty, labelFrame.EndTypes);
                    context.PushControlFrame(CatchOp, labelType);
                    switch (handler.Mode)
                    {
                        case CatchFlags.None: //catch x
                        {
                            var tag = context.Tags[handler.X];
                            var tagType = context.Types[tag.TypeIndex];
                            var compType = tagType.Expansion;
                            var functionType = compType as FunctionType;
                            context.OpStack.PushResult(functionType!.ParameterTypes);
                        } break;
                        case CatchFlags.CatchRef: //catch_ref x
                        {
                            var tag = context.Tags[handler.X];
                            var tagType = context.Types[tag.TypeIndex];
                            var compType = tagType.Expansion;
                            var functionType = compType as FunctionType;
                            context.OpStack.PushResult(functionType!.ParameterTypes);
                            context.OpStack.PushType(ValType.Exn);
                        } break;
                        case CatchFlags.CatchAll: //catch_all
                            break;
                        case CatchFlags.CatchAllRef: //catch_all_ref
                            context.OpStack.PushType(ValType.Exn);
                            break;
                    }
                    context.PopControlFrame();
                }
                
                //ControlStack will push the values back on (Control Frame is our Label)
                context.PushControlFrame(TryTableOp, funcType);
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

        public override void Execute(ExecContext context)
        {
            // context.EnterBlock(this);
            context.Frame.PushLabel(this);
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            var blockType = ValTypeParser.Parse(reader, parseBlockIndex: true, parseStorageType: false);
            Catches = reader.ParseVector(CatchType.Parse);
            Block = new Block(
                blockType: blockType,
                seq: new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction, IsEnd))
            );
            return this;
        }
    }
    
    public class InstThrow : InstructionBase
    {
        private TagIdx X;
        public override ByteCode Op => OpCode.Throw;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tags.Contains(X), 
                "Tag {0} does not exist in the context.", X);
            var tag = context.Tags[X];
            var tagType = context.Types[tag.TypeIndex];
            var compType = tagType.Expansion;
            var functionType = compType as FunctionType;
            context.Assert(functionType,
                "Tag {0} is not a function type.", X);
            context.Assert(functionType.ResultType.Arity == 0,
                "Tag {0} ResultType is not empty.", X);
            
            context.OpStack.DiscardValues(functionType.ParameterTypes);
            context.SetUnreachable();
        }

        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert(context.Frame.Module.TagAddrs.Contains(X), 
                $"Tag {X} does not exist in the context.");
            //3.
            var ta = context.Frame.Module.TagAddrs[X];
            //4.
            context.Assert(context.Store.Contains(ta), 
                $"Tag {X} does not exist in the store.");
            //5.
            var ti = context.Store[ta];
            //6.
            var tagType = ti.Type;
            var compType = tagType.Expansion;
            var funcType = compType as FunctionType;
            //7.
            context.Assert(context.OpStack.Count >= funcType.ParameterTypes.Arity,
                $"Tag {X} expected {funcType.ParameterTypes.Arity} parameters, but only {context.OpStack.Count} were provided.");
            //8.
            var valn = new Stack<Value>();
            context.OpStack.PopResults(funcType.ParameterTypes, ref valn);
            //9.
            var ea = context.Store.AllocateExn(ta, valn);
            //10.
            var exn = context.Store[ea];
            //11.
            context.OpStack.PushValue(new Value(ValType.Exn, exn));
            
            InstThrowRef.ExecuteInstruction(context);
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TagIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstThrowRef : InstructionBase
    {
        public override ByteCode Op => OpCode.ThrowRef;
        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopType(ValType.Exn);
            context.SetUnreachable();
        }

        public override void Execute(ExecContext context)
        {
            ExecuteInstruction(context);
        }
        
        public static void ExecuteInstruction(ExecContext context)
        {
            //1.
            context.Assert(context.OpStack.Peek().IsType(ValType.Exn),
                "Expected (exnref) on top of the stack.");
            //2.
            var exnref = context.OpStack.PopType(ValType.Exn);
            //3.
            if (exnref.IsNullRef)
                throw new TrapException($"Exception reference is null.");
            //4,5,6,7.
            var exn = exnref.GcRef as ExnInstance;
            context.Assert(exn, "Expected (exnref) on top of the stack.");
            //8.
            var a = exn.Tag;
            //9.

            //Traverse the control stack
            while (context.StackHeight > 0)
            {
                //Enumerate all the blocks to find catch clauses
                while (context.Frame.LabelCount > 1)
                {
                    var blockTarget = context.Frame.TopLabel;
                    context.ExitBlock();
                    if (blockTarget is InstTryTable tryTable)
                    {
                        foreach (var handler in tryTable.Catches)
                        {
                            switch (handler.Mode)
                            {
                                case CatchFlags.None:
                                    if (a.Equals(context.Frame.Module.TagAddrs[handler.X]))
                                    {
                                        context.OpStack.PushResults(exn.Fields);
                                        InstBranch.ExecuteInstruction(context, handler.L);
                                        return;
                                    }
                                    break;
                                case CatchFlags.CatchRef:
                                    if (a.Equals(context.Frame.Module.TagAddrs[handler.X]))
                                    {
                                        context.OpStack.PushResults(exn.Fields);
                                        context.OpStack.PushValue(exnref);
                                        InstBranch.ExecuteInstruction(context, handler.L);
                                        return;
                                    }
                                    break;
                                case CatchFlags.CatchAll:
                                    InstBranch.ExecuteInstruction(context, handler.L);
                                    return;
                                case CatchFlags.CatchAllRef:
                                    context.OpStack.PushValue(exnref);
                                    InstBranch.ExecuteInstruction(context, handler.L);
                                    return;
                            }
                        }
                    }
                }
                context.FunctionReturn();
            }

            throw new UnhandledWasmException($"Unhandled exception {exn}");
        }
    }
}