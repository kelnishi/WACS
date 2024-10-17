using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Utilities;


// 2.4.6 Table Instructions
namespace Wacs.Core.Instructions
{
    // 0x25
    public class InstTableGet : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableGet;
        public TableIdx TableIndex { get; private set; }

        // @Spec 3.3.6.1. table.get
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(TableIndex),
                $"Instruction table.get failed to get table {TableIndex} from context");
            var type = context.Tables[TableIndex];
            context.OpStack.PopI32();
            context.OpStack.PushType(type.ElementType.StackType());
        }
        // @Spec 4.4.6.1. table.get 
        public override void Execute(ExecContext context) => ExecuteInstruction(context, TableIndex);
        public static void ExecuteInstruction(ExecContext context, TableIdx tableIndex)
        {
            //2.
            context.Assert(context.Frame.Module.TableAddrs.Contains(tableIndex),
                ()=>$"Instruction table.get could not address table {tableIndex}");
            //3.
            var a = context.Frame.Module.TableAddrs[tableIndex];
            
            //4.
            context.Assert(context.Store.Contains(a),
                ()=>$"Instruction table.get failed to get table at address {a} from Store");
            //5.
            var tab = context.Store[a];
            //6,7
            int i = context.OpStack.PopI32();
            //8.
            if (i >= tab.Elements.Count)
            {
                //TODO: Trap
            }
            //9.
            var val = tab.Elements[i];
            //10.
            context.OpStack.PushValue(val);
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            TableIndex = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }
    }

    // 0x26
    public class InstTableSet : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableSet;
        public TableIdx TableIndex { get; private set; }

        // @Spec 3.3.6.2. table.set
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(TableIndex),
                $"Instruction table.set failed to get table {TableIndex} from context");
            var type = context.Tables[TableIndex];
            context.OpStack.PopType(type.ElementType.StackType());
            context.OpStack.PopI32();
        }
        // @Spec 4.4.6.2. table.set
        public override void Execute(ExecContext context) => ExecuteInstruction(context, TableIndex);
        public static void ExecuteInstruction(ExecContext context, TableIdx tableIndex)
        {
            //2.
            context.Assert(context.Frame.Module.TableAddrs.Contains(tableIndex),
                ()=>$"Instruction table.get could not address table {tableIndex}");
            //3.
            var a = context.Frame.Module.TableAddrs[tableIndex];
            
            //4.
            context.Assert(context.Store.Contains(a),
                ()=>$"Instruction table.set failed to get table at address {a} from Store");
            //5.
            var tab = context.Store[a];
            //6.
            context.Assert(context.OpStack.Peek().IsRef,
                ()=>$"Instruction table.set found non reftype on top of the Stack");
            //7.
            var val = context.OpStack.PopRefType();
            //8.
            context.Assert(context.OpStack.Peek().IsI32,
                ()=>$"Instruction table.set found incorrect type on top of the Stack");
            //9.
            int i = context.OpStack.PopI32();
            //10.
            if (i >= tab.Elements.Count)
            {
                //TODO: Trap
            }
            //11.
            tab.Elements[i] = val;
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            TableIndex = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }
    }

    // 0xFC0C
    public class InstTableInit : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableInit;
        public TableIdx TableIndex { get; internal set; }
        public ElemIdx ElementIndex { get; internal set; } // This may be updated depending on the value type specification

        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(TableIndex),
                $"Instruction table.init is invalid. Table {TableIndex} not in the Context.");
            var t1 = context.Tables[TableIndex];
            context.Assert(context.Elements.Contains(ElementIndex),
                $"Instruction table.init is invalid. Element {ElementIndex} not in the Context.");
            var t2 = context.Elements[ElementIndex];
            context.Assert(t1.ElementType == t2.Type,
                $"Instruction table.init is invalid. Type mismatch {t1.ElementType} != {t2.Type}");
            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopI32();
        }
        
        private const long TwoTo32 = 0x1_0000_0000;
        
        // @Spec 4.4.6.7. table.init x y
        public override void Execute(ExecContext context) => ExecuteInstruction(context, TableIndex, ElementIndex);
        public static void ExecuteInstruction(ExecContext context, TableIdx x, ElemIdx y)
        {
            //2.
            context.Assert(context.Frame.Module.TableAddrs.Contains(x),
                ()=>$"Instruction table.init failed. Table address not found in the context.");
            //3.
            var ta = context.Frame.Module.TableAddrs[x];
            //4.
            context.Assert(context.Store.Contains(ta),
                ()=>$"Instruction table.init failed. Invalid table address.");
            //5.
            var tab = context.Store[ta];
            //6.
            context.Assert(context.Frame.Module.ElemAddrs.Contains(y),
                ()=>$"Instruction table.init failed. Element address not found in the context.");
            //7.
            var ea = context.Frame.Module.ElemAddrs[y];
            //8.
            context.Assert(context.Store.Contains(ea),
                ()=>$"Instruction table.init failed. Invalid element address");
            //9.
            var elem = context.Store[ea];
            //10.
            context.Assert(context.OpStack.Peek().IsI32, 
                ()=>$"Instruction table.init failed. Expected i32 on top of the stack.");
            //11.
            int n = context.OpStack.PopI32();
            //12.
            context.Assert(context.OpStack.Peek().IsI32, 
                ()=>$"Instruction table.init failed. Expected i32 on top of the stack.");
            //13.
            int s = context.OpStack.PopI32();
            //14.
            context.Assert(context.OpStack.Peek().IsI32, 
                ()=>$"Instruction table.init failed. Expected i32 on top of the stack.");
            //15.
            int d = context.OpStack.PopI32();
            //16.
            if (s + n > elem.Elements.Count || d + n > tab.Elements.Count)
            {
                //TODO Trap
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
            InstTableSet.ExecuteInstruction(context, x);
            //22.
            long check = (long)d + 1;
            context.Assert(check < TwoTo32,
                ()=>$"Instruction table.init failed. Invalid table size");
            //23.
            context.OpStack.PushI32(d+1);
            //24.
            check = (long)s + 1;
            context.Assert(check < TwoTo32,
                ()=>$"Instruction table.init failed. Invalid table size");
            //25.
            context.OpStack.PushI32(s+1);
            //26.
            context.OpStack.PushI32(n-1);
            ExecuteInstruction(context,x,y);
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            TableIndex = (TableIdx)reader.ReadLeb128_u32();
            ElementIndex = (ElemIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override IInstruction Immediate(uint x, uint y)
        {
            TableIndex = (TableIdx)x;
            ElementIndex = (ElemIdx)y;
            return this;
        }
    }
    
    // 0xFC0F
    public class InstElemDrop : InstructionBase
    {
        public override OpCode OpCode => OpCode.ElemDrop;
        private ElemIdx ElementIndex { get; set; }

        // @Spec 3.3.6.8. elem.drop
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Elements.Contains(ElementIndex),
                $"Instruction elem.drop is invalid. Element {ElementIndex} was not in the Context");
        }

        public override void Execute(ExecContext context) {
            //2.
            context.Assert(context.Frame.Module.ElemAddrs.Contains(ElementIndex),
                ()=>$"Instruction elem.drop failed. Element {ElementIndex} was not in the context");
            //3.
            var a = context.Frame.Module.ElemAddrs[ElementIndex];
            //4.
            context.Assert(context.Store.Contains(a),
                ()=>$"Instruction elem.drop failed. Element {a} was not in the Store.");
            //5.
            context.Store[a].Drop();
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            ElementIndex = (ElemIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override IInstruction Immediate(int value)
        {
            ElementIndex = (ElemIdx)value;
            return this;
        }
    }
    
    // 0xFC0E
    public class InstTableCopy : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableCopy;
        public TableIdx SrcTableIndex { get; internal set; }
        public TableIdx DstTableIndex { get; internal set; }

        private const long TwoTo32 = 0x1_0000_0000;
        
        // @Spec 3.3.6.6. table.copy
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(SrcTableIndex),
                $"Instruction table.copy failed. Table index {SrcTableIndex} does not exist in Context");
            var t1 = context.Tables[SrcTableIndex];
            context.Assert(context.Tables.Contains(DstTableIndex),
                $"Instruction table.copy failed. Table index {DstTableIndex} does not exist in Context");
            var t2 = context.Tables[DstTableIndex];
            context.Assert(t1.ElementType == t2.ElementType,
                $"Instruction table.copy failed. Table type mismatch {t1.ElementType} != {t2.ElementType}");
            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopI32();
        }
        // @Spec 4.4.6.6. table.copy
        public override void Execute(ExecContext context) => ExecuteInstruction(context, DstTableIndex, SrcTableIndex);
        public static void ExecuteInstruction(ExecContext context, TableIdx x, TableIdx y)
        {
            //2.
            context.Assert(context.Frame.Module.TableAddrs.Contains(x),
                ()=>$"Instruction table.copy did not find source table {x} in the Context");
            //3.
            var taX = context.Frame.Module.TableAddrs[x];
            //4.
            context.Assert(context.Store.Contains(taX),
                ()=>$"Instruction table.copy failed. Address was not present in the Store.");
            //5.
            var tabX = context.Store[taX];
            //6.
            context.Assert(context.Frame.Module.TableAddrs.Contains(y),
                ()=>$"Instruction table.copy did not find destination table {y} in the Context");
            //7.
            var taY = context.Frame.Module.TableAddrs[y];
            //8.
            context.Assert(context.Store.Contains(taY),
                ()=>$"Instruction table.copy failed. Address was not present in the Store.");
            //9.
            var tabY = context.Store[taY];
            //10.
            context.Assert(context.OpStack.Peek().IsI32,
                ()=>$"Instruction table.copy failed. Expected i32 on top of the stack.");
            //11.
            int n = context.OpStack.PopI32();
            //12.
            context.Assert(context.OpStack.Peek().IsI32,
                ()=>$"Instruction table.copy failed. Expected i32 on top of the stack.");
            //13.
            int s = context.OpStack.PopI32();
            //14.
            context.Assert(context.OpStack.Peek().IsI32,
                ()=>$"Instruction table.copy failed. Expected i32 on top of the stack.");
            //15.
            int d = context.OpStack.PopI32();
            //16.
            if (s + n > tabY.Elements.Count || d + n > tabX.Elements.Count)
            {
                //TODO Trap
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
                InstTableGet.ExecuteInstruction(context, y);
                InstTableSet.ExecuteInstruction(context, x);
                long check = (long)d + 1;
                context.Assert(check < TwoTo32,
                    ()=>"Intruction table.copy failed. Table size overflow");
                context.OpStack.PushI32(d+1);
                // context.Assert((long)s+1 < TwoTo32,
                //     "Intruction table.copy failed. Table size overflow");
                context.OpStack.PushI32(s+1);
            }
            //19.
            else
            {
                long check = (long)d + n - 1;
                context.Assert(check < TwoTo32,
                    ()=>"Intruction table.copy failed. Table size overflow");
                context.OpStack.PushI32(d+n-1);
                check = (long)s + n - 1;
                context.Assert(check < TwoTo32,
                    ()=>"Intruction table.copy failed. Table size overflow");
                context.OpStack.PushI32(s+n-1);
                InstTableGet.ExecuteInstruction(context, y);
                InstTableSet.ExecuteInstruction(context, x);
                context.OpStack.PushI32(d);
                context.OpStack.PushI32(s);
            }
            //20.
            context.OpStack.PushI32(n-1);
            //21.
            ExecuteInstruction(context, x, y);

        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            DstTableIndex = (TableIdx)reader.ReadLeb128_u32();
            SrcTableIndex = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    // 0xFC0F
    public class InstTableGrow : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableGrow;
        public TableIdx TableIndex { get; internal set; }

        // @Spec 3.3.6.4. table.grow
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(TableIndex),
                $"Instruction table.set failed to get table {TableIndex} from context");
            var type = context.Tables[TableIndex];
            context.OpStack.PopI32();
            context.OpStack.PopType(type.ElementType.StackType());
            context.OpStack.PushI32();
        }

        // @Spec 4.4.6.4. table.grow
        public override void Execute(ExecContext context) {
            //2.
            context.Assert(context.Frame.Module.TableAddrs.Contains(TableIndex),
                ()=>$"Instruction table.get could not address table {TableIndex}");
            //3.
            var addr = context.Frame.Module.TableAddrs[TableIndex];
            
            //4.
            context.Assert(context.Store.Contains(addr),
                ()=>$"Instruction table.set failed to get table at address {addr} from Store");
            //5.
            var tab = context.Store[addr];
            //6.
            int sz = tab.Elements.Count;
            //7.
            context.Assert(context.OpStack.Peek().IsI32,
                ()=>"Instruction table.grow found incorrect type on top of the Stack");
            //8.
            int n = context.OpStack.PopI32();
            //9.
            context.Assert(context.OpStack.Peek().IsRef,
                ()=>"Instruction table.grow found incorrect type on top of the Stack");
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
                int err = -1;
                context.OpStack.PushI32(err);
            }
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader)
        {
            TableIndex = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    // 0xFC10
    public class InstTableSize : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableSize;
        public TableIdx TableIndex { get; internal set; }

        // @Spec 3.3.6.3. table.size
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(TableIndex),
                $"Instruction table.set failed to get table {TableIndex} from context");
            var type = context.Tables[TableIndex];
            context.OpStack.PushI32();
        }
        public override void Execute(ExecContext context) {
            //2.
            context.Assert(context.Frame.Module.TableAddrs.Contains(TableIndex),
                ()=>$"Instruction table.get could not address table {TableIndex}");
            //3.
            var addr = context.Frame.Module.TableAddrs[TableIndex];
            
            //4.
            context.Assert(context.Store.Contains(addr),
                ()=>$"Instruction table.set failed to get table at address {addr} from Store");
            //5.
            var tab = context.Store[addr];
            //6.
            int sz = tab.Elements.Count;
            //7.
            context.OpStack.PushI32(sz);
        }

        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader) {
            TableIndex = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    // 0xFC11
    public class InstTableFill : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableFill;
        public TableIdx TableIndex { get; internal set; }

        // @Spec 3.3.6.5. table.fill
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Tables.Contains(TableIndex),
                $"Instruction table.set failed to get table {TableIndex} from context");
            var type = context.Tables[TableIndex];
            context.OpStack.PopI32();
            context.OpStack.PopType(type.ElementType.StackType());
            context.OpStack.PopI32();
        }
        
        // @Spec 4.4.6.5. table.fill
        public override void Execute(ExecContext context) => ExecuteInstruction(context, TableIndex);
        public static void ExecuteInstruction(ExecContext context, TableIdx tableIndex)
        {
            //2.
            context.Assert(context.Frame.Module.TableAddrs.Contains(tableIndex),
                ()=>$"Instruction table.get could not address table {tableIndex}");
            //3.
            var addr = context.Frame.Module.TableAddrs[tableIndex];
            
            //4.
            context.Assert(context.Store.Contains(addr),
                ()=>$"Instruction table.set failed to get table at address {addr} from Store");
            //5.
            var tab = context.Store[addr];
            //6.
            context.Assert(context.OpStack.Peek().IsI32,
                ()=>"Instruction table.grow found incorrect type on top of the Stack");
            //7.
            int n = context.OpStack.PopI32();
            //8.
            context.Assert(context.OpStack.Peek().IsRef,
                ()=>"Instruction table.grow found incorrect type on top of the Stack");
            //9.
            var val = context.OpStack.PopRefType();
            //10.
            context.Assert(context.OpStack.Peek().IsI32,
                ()=>"Instruction table.grow found incorrect type on top of the Stack");
            //11.
            int i = context.OpStack.PopI32();
            //12.
            if (i + n > tab.Elements.Count)
            {
                //TODO Trap
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
            InstTableSet.ExecuteInstruction(context, tableIndex);
            //16.
            context.OpStack.PushI32(i+1);
            //17.
            context.OpStack.PushValue(val);
            //18.
            context.OpStack.PushI32(n-1);
            //19.
            ExecuteInstruction(context, tableIndex);
        }
        
        // @Spec 5.4.5. Table Instructions
        public override IInstruction Parse(BinaryReader reader) {
            TableIndex = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
}
