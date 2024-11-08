using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;


// 2.4.6 Table Instructions
namespace Wacs.Core.Instructions
{
    // 0x25
    public class InstTableGet : InstructionBase
    {
        public override ByteCode Op => OpCode.TableGet;
        private TableIdx X { get; set; }

        // @Spec 3.3.6.1. table.get
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                 $"Instruction table.get failed to get table {X} from context");
            var type = context.Tables[X];
            context.OpStack.PopI32();
            context.OpStack.PushType(type.ElementType.StackType());
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
            context.Assert( context.OpStack.Peek().IsI32,
                 $"Instruction table.get failed. Wrong type on stack.");
            //7.
            int i = context.OpStack.PopI32();
            //8.
            if (i >= tab.Elements.Count)
            {
                throw new TrapException("Trap in table.get");
            }

            //9.
            var val = tab.Elements[i];
            //10.
            context.OpStack.PushValue(val);
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    // 0x26
    public class InstTableSet : InstructionBase
    {
        public override ByteCode Op => OpCode.TableSet;
        private TableIdx X { get; set; }

        // @Spec 3.3.6.2. table.set
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                 $"Instruction table.set failed to get table {X} from context");
            var type = context.Tables[X];
            context.OpStack.PopType(type.ElementType.StackType());
            context.OpStack.PopI32();
        }

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
            context.Assert( context.OpStack.Peek().IsRef,
                 $"Instruction table.set found non reftype on top of the Stack");
            //7.
            var val = context.OpStack.PopRefType();
            //8.
            context.Assert( context.OpStack.Peek().IsI32,
                 $"Instruction table.set found incorrect type on top of the Stack");
            //9.
            uint i = context.OpStack.PopI32();
            //10.
            if (i >= tab.Elements.Count)
            {
                throw new TrapException("table.set index exceeds table size.");
            }

            //11.
            tab.Elements[(int)i] = val;
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    // 0xFC0C
    public class InstTableInit : InstructionBase
    {
        public override ByteCode Op => ExtCode.TableInit;
        private TableIdx X { get; set; }
        private ElemIdx Y { get; set; }

        // @Spec 3.3.6.7. table.init x y
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                 $"Instruction table.init is invalid. Table {X} not in the Context.");
            var t1 = context.Tables[X];
            context.Assert(context.Elements.Contains(Y),
                 $"Instruction table.init is invalid. Element {Y} not in the Context.");
            var t2 = context.Elements[Y];
            context.Assert(t1.ElementType == t2.Type,
                 $"Instruction table.init is invalid. Type mismatch {t1.ElementType} != {t2.Type}");
            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopI32();
        }

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
            //6.
            context.Assert( context.Frame.Module.ElemAddrs.Contains(Y),
                 $"Instruction table.init failed. Element address not found in the context.");
            //7.
            var ea = context.Frame.Module.ElemAddrs[Y];
            //8.
            context.Assert( context.Store.Contains(ea),  $"Instruction table.init failed. Invalid element address");
            //9.
            var elem = context.Store[ea];

            //Tail recursive call alternative loop
            while (true)
            {
                //10.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction table.init failed. Expected i32 on top of the stack.");
                //11.
                int n = context.OpStack.PopI32();
                //12.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction table.init failed. Expected i32 on top of the stack.");
                //13.
                int s = context.OpStack.PopI32();
                //14.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction table.init failed. Expected i32 on top of the stack.");
                //15.
                int d = context.OpStack.PopI32();
                //16.
                if (s + n > elem.Elements.Count || d + n > tab.Elements.Count)
                {
                    throw new OutOfBoundsTableAccessException("Trap in table.init");
                }
                else if (n == 0)
                {
                    return;
                }

                //18.
                var val = elem.Elements[s];
                //19.
                context.OpStack.PushI32(d);
                //20.
                context.OpStack.PushRef(val);
                //21.
                InstTableSet.ExecuteInstruction(context, X);
                //22.
                long check = (long)d + 1;
                context.Assert( check < Constants.TwoTo32,  $"Instruction table.init failed. Invalid table size");
                //23.
                context.OpStack.PushI32(d + 1);
                //24.
                check = (long)s + 1;
                context.Assert( check < Constants.TwoTo32,  $"Instruction table.init failed. Invalid table size");
                //25.
                context.OpStack.PushI32(s + 1);
                //26.
                context.OpStack.PushI32(n - 1);
                //27.
            }
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            //!!! `table.init x y` is parsed y then x
            Y = (ElemIdx)reader.ReadLeb128_u32();
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public IInstruction Immediate(TableIdx x, ElemIdx y)
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
        public override ByteCode Op => ExtCode.ElemDrop;
        private ElemIdx X { get; set; }

        // @Spec 3.3.6.8. elem.drop x
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Elements.Contains(X),
                 $"Instruction elem.drop is invalid. Element {X} was not in the Context");
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
        public override IInstruction Parse(BinaryReader reader)
        {
            X = (ElemIdx)reader.ReadLeb128_u32();
            return this;
        }

        public IInstruction Immediate(ElemIdx value)
        {
            X = value;
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    // 0xFC0E
    public class InstTableCopy : InstructionBase
    {
        public override ByteCode Op => ExtCode.TableCopy;
        private TableIdx SrcY { get; set; }
        private TableIdx DstX { get; set; }

        // @Spec 3.3.6.6. table.copy
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(DstX),
                 $"Instruction table.copy failed. Table index {DstX} does not exist in Context");
            var t1 = context.Tables[DstX];
            context.Assert(context.Tables.Contains(SrcY),
                 $"Instruction table.copy failed. Table index {SrcY} does not exist in Context");
            var t2 = context.Tables[SrcY];
            context.Assert(t1.ElementType == t2.ElementType,
                 $"Instruction table.copy failed. Table type mismatch {t1.ElementType} != {t2.ElementType}");
            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopI32();
        }

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

            //Tail recursive call alternative loop
            while (true)
            {
                //10.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction table.copy failed. Expected i32 on top of the stack.");
                //11.
                int n = context.OpStack.PopI32();
                //12.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction table.copy failed. Expected i32 on top of the stack.");
                //13.
                int s = context.OpStack.PopI32();
                //14.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction table.copy failed. Expected i32 on top of the stack.");
                //15.
                int d = context.OpStack.PopI32();
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
                    context.OpStack.PushI32(d);
                    context.OpStack.PushI32(s);
                    InstTableGet.ExecuteInstruction(context, SrcY);
                    InstTableSet.ExecuteInstruction(context, DstX);
                    long check = (long)d + 1;
                    context.Assert( check < Constants.TwoTo32,
                         "Instruction table.copy failed. Table size overflow");
                    context.OpStack.PushI32(d + 1);
                    check = (long)s + 1;
                    context.Assert( check < Constants.TwoTo32,
                         "Instruction table.copy failed. Table size overflow");
                    context.OpStack.PushI32(s + 1);
                }
                //19.
                else
                {
                    long check = (long)d + n - 1;
                    context.Assert( check < Constants.TwoTo32,
                         "Intruction table.copy failed. Table size overflow");
                    context.OpStack.PushI32(d + n - 1);
                    check = (long)s + n - 1;
                    context.Assert( check < Constants.TwoTo32,
                         "Intruction table.copy failed. Table size overflow");
                    context.OpStack.PushI32(s + n - 1);
                    InstTableGet.ExecuteInstruction(context, SrcY);
                    InstTableSet.ExecuteInstruction(context, DstX);
                    context.OpStack.PushI32(d);
                    context.OpStack.PushI32(s);
                }

                //20.
                context.OpStack.PushI32(n - 1);
                //21.
            }
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
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
        public override ByteCode Op => ExtCode.TableGrow;
        private TableIdx X { get; set; }

        // @Spec 3.3.6.4. table.grow x
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                 $"Instruction table.set failed to get table {X} from context");
            var type = context.Tables[X];
            context.OpStack.PopI32();
            context.OpStack.PopType(type.ElementType.StackType());
            context.OpStack.PushI32();
        }

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
            //6.
            int sz = tab.Elements.Count;
            //7.
            context.Assert( context.OpStack.Peek().IsI32,
                 "Instruction table.grow found incorrect type on top of the Stack");
            //8.
            int n = context.OpStack.PopI32();
            //9.
            context.Assert( context.OpStack.Peek().IsRef,
                 "Instruction table.grow found incorrect type on top of the Stack");
            //10.
            var val = context.OpStack.PopRefType();
            //12, 13. TODO: implement optional constraints on table.grow
            if (tab.Grow(n, val))
            {
                context.OpStack.PushI32(sz);
            }
            else
            {
                //11.
                const int err = -1;
                context.OpStack.PushI32(err);
            }
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    // 0xFC10
    public class InstTableSize : InstructionBase
    {
        public override ByteCode Op => ExtCode.TableSize;
        private TableIdx X { get; set; }

        // @Spec 3.3.6.3. table.size x
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                 $"Instruction table.set failed to get table {X} from context");
            context.OpStack.PushI32();
        }

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
            //6.
            int sz = tab.Elements.Count;
            //7.
            context.OpStack.PushI32(sz);
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    // 0xFC11
    public class InstTableFill : InstructionBase
    {
        public override ByteCode Op => ExtCode.TableFill;
        private TableIdx X { get; set; }

        // @Spec 3.3.6.5. table.fill
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(X),
                 $"Instruction table.set failed to get table {X} from context");
            var type = context.Tables[X];
            context.OpStack.PopI32();
            context.OpStack.PopType(type.ElementType.StackType());
            context.OpStack.PopI32();
        }

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

            //Tail recursive call alternative loop
            while (true)
            {
                //6.
                context.Assert( context.OpStack.Peek().IsI32,
                     "Instruction table.grow found incorrect type on top of the Stack");
                //7.
                int n = context.OpStack.PopI32();
                //8.
                context.Assert( context.OpStack.Peek().IsRef,
                     "Instruction table.grow found incorrect type on top of the Stack");
                //9.
                var val = context.OpStack.PopRefType();
                //10.
                context.Assert( context.OpStack.Peek().IsI32,
                     "Instruction table.grow found incorrect type on top of the Stack");
                //11.
                int i = context.OpStack.PopI32();
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
                context.OpStack.PushI32(i);
                //14.
                context.OpStack.PushValue(val);
                //15.
                InstTableSet.ExecuteInstruction(context, X);
                //16.
                context.OpStack.PushI32(i + 1);
                //17.
                context.OpStack.PushValue(val);
                //18.
                context.OpStack.PushI32(n - 1);
                //19.
            }
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }
}