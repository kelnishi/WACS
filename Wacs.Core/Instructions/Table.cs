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

using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;


// 2.4.6 Table Instructions
namespace Wacs.Core.Instructions
{
    // 0x25
    public class InstTableGet : InstructionBase
    {
        private TableIdx X;
        public override ByteCode Op => OpCode.TableGet;

        // @Spec 3.3.6.1. table.get
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                "Instruction table.get failed to get table {0} from context",X);
            var type = context.Tables[X];
            var at = type.Limits.AddressType;
            context.OpStack.PopType(at.ToValType());    // -1
            context.OpStack.PushType(type.ElementType); // +0
        }

        // @Spec 4.4.6.1. table.get 
        public override void Execute(ExecContext context) => ExecuteInstruction(context, X);

        public static void ExecuteInstruction(ExecContext context, TableIdx tableIndex)
        {
            //2.
            context.Assert( context.Frame.Module.TableAddrs.Contains(tableIndex),
                $"Instruction table.get could not address table {tableIndex}");
            //3.
            var a = context.Frame.Module.TableAddrs[tableIndex];

            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction table.get failed to get table at address {a} from Store");
            //5.
            var tab = context.Store[a];
            //6.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction table.get failed. Wrong type on stack.");
            //7.
            long i = context.OpStack.PopAddr();
            //8.
            if (i >= tab.Elements.Count || (i > (long)int.MaxValue))
            {
                throw new TrapException("Trap in table.get");
            }

            //9.
            var val = tab.Elements[(int)i];
            //10.
            context.OpStack.PushValue(val);
        }

        // @Spec 5.4.5. Table Instructions
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    // 0x26
    public class InstTableSet : InstructionBase
    {
        private TableIdx X;
        public override ByteCode Op => OpCode.TableSet;

        // @Spec 3.3.6.2. table.set
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                "Instruction table.set failed to get table {0} from context", X);
            var type = context.Tables[X];
            var at = type.Limits.AddressType;
            context.OpStack.PopType(type.ElementType);  // -1
            context.OpStack.PopType(at.ToValType());    // -2
        }
        protected override int StackDiff => -2;

        // @Spec 4.4.6.2. table.set
        public override void Execute(ExecContext context) => ExecuteInstruction(context, X);

        public static void ExecuteInstruction(ExecContext context, TableIdx tableIndex)
        {
            //2.
            context.Assert( context.Frame.Module.TableAddrs.Contains(tableIndex),
                $"Instruction table.get could not address table {tableIndex}");
            //3.
            var a = context.Frame.Module.TableAddrs[tableIndex];

            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction table.set failed to get table at address {a} from Store");
            //5.
            var tab = context.Store.GetMutableTable(a);
            //6.
            context.Assert( context.OpStack.Peek().IsRefType,
                $"Instruction table.set found non reftype on top of the Stack");
            //7.
            var val = context.OpStack.PopRefType();
            //8.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction table.set found incorrect type on top of the Stack");
            //9.
            long i = context.OpStack.PopAddr();
            //10.
            if (i >= tab.Elements.Count)
            {
                throw new TrapException("table.set index exceeds table size.");
            }

            //11.
            tab.Elements[(int)i] = val;
        }

        // @Spec 5.4.5. Table Instructions
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    // 0xFC0C
    public class InstTableInit : InstructionBase
    {
        private TableIdx X;
        private ElemIdx Y;
        public override ByteCode Op => ExtCode.TableInit;

        // @Spec 3.3.6.7. table.init x y
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                "Instruction table.init is invalid. Table {0} not in the Context.", X);
            var t1 = context.Tables[X];
            context.Assert(context.Elements.Contains(Y),
                "Instruction table.init is invalid. Element {0} not in the Context.",Y);
            var t2 = context.Elements[Y];
            context.Assert(t2.Type.Matches(t1.ElementType, context.Types),
                "Instruction table.init is invalid. Type mismatch {0} != {1}",t1.ElementType,t2.Type);
            context.OpStack.PopI32();   // -1
            context.OpStack.PopI32();   // -2

            var at = t1.Limits.AddressType;
            context.OpStack.PopType(at.ToValType()); // -3
        }
        protected override int StackDiff => -3;

        // @Spec 4.4.6.7. table.init x y
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.TableAddrs.Contains(X),
                $"Instruction table.init failed. Table address not found in the context.");
            //3.
            var ta = context.Frame.Module.TableAddrs[X];
            //4.
            context.Assert( context.Store.Contains(ta),  $"Instruction table.init failed. Invalid table address.");
            //5.
            var tab = context.Store.GetMutableTable(ta);
            var at = tab.Type.Limits.AddressType;
            //6.
            context.Assert( context.Frame.Module.ElemAddrs.Contains(Y),
                $"Instruction table.init failed. Element address not found in the context.");
            //7.
            var ea = context.Frame.Module.ElemAddrs[Y];
            //8.
            context.Assert( context.Store.Contains(ea),  $"Instruction table.init failed. Invalid element address");
            //9.
            var elem = context.Store[ea];

            //10.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Expected i32 on top of the stack.");
            //11.
            long n = (uint)context.OpStack.PopI32();
            //12.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Expected i32 on top of the stack.");
            //13.
            long s = (uint)context.OpStack.PopI32();
            //14.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} found incorrect type on top of the Stack");
            //15.
            long d = context.OpStack.PopAddr();
            
            if (s + n > elem.Elements.Count || d + n > tab.Elements.Count)
            {
                throw new OutOfBoundsTableAccessException("Trap in table.init");
            }
            
            //Tail recursive call alternative loop, inline tableset
            while (true)
            {
                if (n == 0)
                    return;

                //Set table element direct
                tab.Elements[(int)d] = elem.Elements[(int)s];
                
                d += 1L;
                s += 1L;
                n -= 1L;
            }
        }

        // @Spec 5.4.5. Table Instructions
        public override InstructionBase Parse(BinaryReader reader)
        {
            //!!! `table.init x y` is parsed y then x
            Y = (ElemIdx)reader.ReadLeb128_u32();
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public InstructionBase Immediate(TableIdx x, ElemIdx y)
        {
            X = x;
            Y = y;
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value} {Y.Value}";
    }

    // 0xFC0F
    public class InstElemDrop : InstructionBase
    {
        private ElemIdx X;
        public override ByteCode Op => ExtCode.ElemDrop;

        // @Spec 3.3.6.8. elem.drop x
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Elements.Contains(X),
                "Instruction elem.drop is invalid. Element {0} was not in the Context",X);
        }

        // @Spec 4.4.6.8. elem.drop x
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.ElemAddrs.Contains(X),
                $"Instruction elem.drop failed. Element {X} was not in the context");
            //3.
            var a = context.Frame.Module.ElemAddrs[X];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction elem.drop failed. Element {a} was not in the Store.");
            //5.
            context.Store.DropElement(a);
        }

        // @Spec 5.4.5. Table Instructions
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (ElemIdx)reader.ReadLeb128_u32();
            return this;
        }

        public InstructionBase Immediate(ElemIdx value)
        {
            X = value;
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    // 0xFC0E
    public class InstTableCopy : InstructionBase
    {
        private TableIdx DstX;
        private TableIdx SrcY;
        public override ByteCode Op => ExtCode.TableCopy;

        // @Spec 3.3.6.6. table.copy
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(DstX),
                "Instruction table.copy failed. Table index {0} does not exist in Context",DstX);
            var t1 = context.Tables[DstX];
            context.Assert(context.Tables.Contains(SrcY),
                "Instruction table.copy failed. Table index {0} does not exist in Context",SrcY);
            var t2 = context.Tables[SrcY];
            context.Assert(t2.ElementType.Matches(t1.ElementType, context.Types),
                "Instruction table.copy failed. Table type mismatch {0} != {1}",t1.ElementType,t2.ElementType);
            
            var at1 = t1.Limits.AddressType;
            var at2 = t2.Limits.AddressType;
            var at = at1.Min(at2);
            
            context.OpStack.PopType(at.ToValType());    // -1
            context.OpStack.PopType(at2.ToValType());   // -2
            context.OpStack.PopType(at1.ToValType());   // -3
        }
        protected override int StackDiff => -3;

        // @Spec 4.4.6.6. table.copy
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.TableAddrs.Contains(DstX),
                $"Instruction table.copy did not find source table {DstX} in the Context");
            //3.
            var taX = context.Frame.Module.TableAddrs[DstX];
            //4.
            context.Assert( context.Store.Contains(taX),
                $"Instruction table.copy failed. Address was not present in the Store.");
            //5.
            var tabX = context.Store.GetMutableTable(taX);
            var atD = tabX.Type.Limits.AddressType;
            //6.
            context.Assert( context.Frame.Module.TableAddrs.Contains(SrcY),
                $"Instruction table.copy did not find destination table {SrcY} in the Context");
            //7.
            var taY = context.Frame.Module.TableAddrs[SrcY];
            //8.
            context.Assert( context.Store.Contains(taY),
                $"Instruction table.copy failed. Address was not present in the Store.");
            //9.
            var tabY = context.Store[taY];
            var atS = tabY.Type.Limits.AddressType;

            var at = atD.Min(atS);
            
            //Tail recursive call alternative loop
            while (true)
            {
                //10.
                context.Assert( context.OpStack.Peek().IsInt,
                    $"Instruction {Op.GetMnemonic()} found incorrect type on top of the Stack");
                //11.
                long n = context.OpStack.PopAddr();
                //12.
                context.Assert( context.OpStack.Peek().IsInt,
                    $"Instruction {Op.GetMnemonic()} found incorrect type on top of the Stack");
                //13.
                long s = context.OpStack.PopAddr();
                //14.
                context.Assert( context.OpStack.Peek().IsInt,
                    $"Instruction {Op.GetMnemonic()} found incorrect type on top of the Stack");
                //15.
                long d = context.OpStack.PopAddr();
                //16.
                if (s + n > tabY.Elements.Count || d + n > tabX.Elements.Count)
                {
                    throw new TrapException("Trap in table.copy");
                }
                //17.
                else if (n == 0)
                {
                    return;
                }

                //18.
                if (d <= s)
                {
                    context.OpStack.PushValue(new Value(atD, d));
                    context.OpStack.PushValue(new Value(atS,s));
                    InstTableGet.ExecuteInstruction(context, SrcY);
                    InstTableSet.ExecuteInstruction(context, DstX);
                    long check = d + 1L;
                    context.Assert( check < Constants.TwoTo32,
                        "Instruction table.copy failed. Table size overflow");
                    context.OpStack.PushValue(new Value(atD, d + 1L));
                    check = s + 1L;
                    context.Assert( check < Constants.TwoTo32,
                        "Instruction table.copy failed. Table size overflow");
                    context.OpStack.PushValue(new Value(atS, s + 1L));
                }
                //19.
                else
                {
                    long check = d + n - 1L;
                    context.Assert( check < Constants.TwoTo32,
                        "Intruction table.copy failed. Table size overflow");
                    context.OpStack.PushValue(new Value(atD, d + n - 1L));
                    check = (long)s + n - 1;
                    context.Assert( check < Constants.TwoTo32,
                        "Intruction table.copy failed. Table size overflow");
                    context.OpStack.PushValue(new Value(atS, s + n - 1L));
                    InstTableGet.ExecuteInstruction(context, SrcY);
                    InstTableSet.ExecuteInstruction(context, DstX);
                    context.OpStack.PushValue(new Value(atD, d));
                    context.OpStack.PushValue(new Value(atS, s));
                }

                //20.
                context.OpStack.PushValue(new Value(at, n - 1L));
                //21.
            }
        }

        // @Spec 5.4.5. Table Instructions
        public override InstructionBase Parse(BinaryReader reader)
        {
            DstX = (TableIdx)reader.ReadLeb128_u32();
            SrcY = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {DstX.Value} {SrcY.Value}";
    }

    // 0xFC0F
    public class InstTableGrow : InstructionBase
    {
        private TableIdx X;
        public override ByteCode Op => ExtCode.TableGrow;

        // @Spec 3.3.6.4. table.grow x
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                "Instruction table.grow failed to get table {0} from context",X);
            var type = context.Tables[X];
            var at = type.Limits.AddressType;
            
            context.OpStack.PopType(at.ToValType());    // -1
            context.OpStack.PopType(type.ElementType);  // -2
            context.OpStack.PushType(at.ToValType());   // -1
        }
        protected override int StackDiff => -1;

        // @Spec 4.4.6.4. table.grow x
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.TableAddrs.Contains(X),
                $"Instruction table.get could not address table {X}");
            //3.
            var addr = context.Frame.Module.TableAddrs[X];

            //4.
            context.Assert( context.Store.Contains(addr),
                $"Instruction table.set failed to get table at address {addr} from Store");
            //5.
            var tab = context.Store.GetMutableTable(addr);
            var at = tab.Type.Limits.AddressType;
            //6.
            long sz = tab.Elements.Count;
            //7.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} found incorrect type on top of the Stack");
            //8.
            long n = context.OpStack.PopAddr();
            //9.
            context.Assert( context.OpStack.Peek().IsRefType,
                $"Instruction {Op.GetMnemonic()} found incorrect type on top of the Stack");
            //10.
            var val = context.OpStack.PopRefType();
            //12, 13. TODO: implement optional constraints on table.grow
            if (tab.Grow(n, val))
            {
                context.OpStack.PushValue(new Value(at, sz));
            }
            else
            {
                //11.
                const int err = -1;
                context.OpStack.PushValue(new Value(at, err));
            }
        }

        // @Spec 5.4.5. Table Instructions
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    // 0xFC10
    public class InstTableSize : InstructionBase
    {
        private TableIdx X;
        public override ByteCode Op => ExtCode.TableSize;

        // @Spec 3.3.6.3. table.size x
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                "Instruction table.set failed to get table {0} from context",X);
            var table = context.Tables[X];
            var at = table.Limits.AddressType;
            context.OpStack.PushType(at.ToValType());   // +1
        }
        protected override int StackDiff => +1;

        // @Spec 4.4.6.3. table.size x
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.TableAddrs.Contains(X),
                $"Instruction table.get could not address table {X}");
            //3.
            var addr = context.Frame.Module.TableAddrs[X];

            //4.
            context.Assert( context.Store.Contains(addr),
                $"Instruction table.set failed to get table at address {addr} from Store");
            //5.
            var tab = context.Store[addr];
            var at = tab.Type.Limits.AddressType;
            //6.
            long sz = tab.Elements.Count;
            //7.
            context.OpStack.PushValue(new Value(at, sz));
        }

        // @Spec 5.4.5. Table Instructions
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    // 0xFC11
    public class InstTableFill : InstructionBase
    {
        private TableIdx X;
        public override ByteCode Op => ExtCode.TableFill;

        // @Spec 3.3.6.5. table.fill
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                "Instruction table.set failed to get table {0} from context",X);
            var type = context.Tables[X];
            var at = type.Limits.AddressType;
            context.OpStack.PopType(at.ToValType());    // -1
            context.OpStack.PopType(type.ElementType);  // -2
            context.OpStack.PopType(at.ToValType());    // -3
        }
        protected override int StackDiff => -3;

        // @Spec 4.4.6.5. table.fill
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.TableAddrs.Contains(X),
                $"Instruction table.get could not address table {X}");
            //3.
            var addr = context.Frame.Module.TableAddrs[X];

            //4.
            context.Assert( context.Store.Contains(addr),
                $"Instruction table.set failed to get table at address {addr} from Store");
            //5.
            var tab = context.Store.GetMutableTable(addr);
            var at = tab.Type.Limits.AddressType;

            //Tail recursive call alternative loop
            while (true)
            {
                //6.
                context.Assert( context.OpStack.Peek().IsInt,
                    $"Instruction {Op.GetMnemonic()} found incorrect type on top of the Stack");
                //7.
                long n = context.OpStack.PopAddr();
                //8.
                context.Assert( context.OpStack.Peek().IsRefType,
                    $"Instruction {Op.GetMnemonic()} found incorrect type on top of the Stack");
                //9.
                var val = context.OpStack.PopRefType();
                //10.
                context.Assert( context.OpStack.Peek().IsInt,
                    $"Instruction {Op.GetMnemonic()} found incorrect type on top of the Stack");
                //11.
                long i = context.OpStack.PopAddr();
                //12.
                if (i + n > tab.Elements.Count)
                {
                    throw new TrapException("Trap in table.fill");
                }
                else if (n == 0)
                {
                    return;
                }

                //13.
                context.OpStack.PushValue(new Value(at, i));
                //14.
                context.OpStack.PushValue(val);
                //15.
                InstTableSet.ExecuteInstruction(context, X);
                //16.
                context.OpStack.PushValue(new Value(at, i + 1));
                //17.
                context.OpStack.PushValue(val);
                //18.
                context.OpStack.PushValue(new Value(at, n - 1));
                //19.
            }
        }

        // @Spec 5.4.5. Table Instructions
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }
}