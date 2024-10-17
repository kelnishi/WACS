using System;
using System.IO;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// 5.4.4 Variable Instructions
namespace Wacs.Core.Instructions
{
    public class LocalVariableInst : InstructionBase
    {
        public override OpCode OpCode { get; }
        public LocalIdx Index { get; internal set; }
        
        public delegate void ValidationDelegate(WasmValidationContext context, LocalIdx localIndex);
        public delegate void ExecuteDelegate(ExecContext context, LocalIdx localIndex);

        private ValidationDelegate _validate;
        private ExecuteDelegate _execute;
        public LocalVariableInst(OpCode opCode, ExecuteDelegate execute, ValidationDelegate validate) =>
            (_execute, _validate) = (execute, validate);

        public override void Validate(WasmValidationContext context) => _validate(context, Index);
        public override void Execute(ExecContext context) => _execute(context, Index);
        
        public override IInstruction Parse(BinaryReader reader)
        {
            Index = (LocalIdx)reader.ReadLeb128_u32();
            return this;
        }

        public static LocalVariableInst CreateInstLocalGet() => new LocalVariableInst(OpCode.LocalGet, ExecuteLocalGet, ValidateLocalGet);
        public static LocalVariableInst CreateInstLocalSet() => new LocalVariableInst(OpCode.LocalSet, ExecuteLocalSet, ValidateLocalSet);
        public static LocalVariableInst CreateInstLocalTee() => new LocalVariableInst(OpCode.LocalTee, ExecuteLocalTee, ValidateLocalTee);
        
        
        //0x20
        // @Spec 3.3.5.1. local.get
        private static void ValidateLocalGet(WasmValidationContext context, LocalIdx localIndex)
        {
            context.Assert(context.Locals.Contains(localIndex),
                $"Instruction local.get was invalid. Context Locals did not contain {localIndex}");
            var value = context.Locals[localIndex];
            context.OpStack.PushType(value.Type);
        }
        // @Spec 4.4.5.1. local.get 
        private static void ExecuteLocalGet(ExecContext context, LocalIdx localIndex)
        {
            //2.
            context.Assert(context.Frame.Locals.Contains(localIndex),
                ()=>$"Instruction local.get could not get Local {localIndex}");
            //3.
            var value = context.Frame.Locals[localIndex];
            //4.
            context.OpStack.PushValue(value);
        }
        
        //0x21
        private static void ValidateLocalSet(WasmValidationContext context, LocalIdx localIndex)
        {
            context.Assert(context.Locals.Contains(localIndex),
                $"Instruction local.set was invalid. Context Locals did not contain {localIndex}");
            var value = context.Locals[localIndex];
            context.OpStack.PopType(value.Type);
        }
        // @Spec 4.4.5.2. local.set
        private static void ExecuteLocalSet(ExecContext context, LocalIdx localIndex)
        {
            //2.
            context.Assert(context.Frame.Locals.Contains(localIndex),
                ()=>$"Instruction local.get could not get Local {localIndex}");
            //3.
            context.Assert(context.OpStack.HasValue,
                ()=>$"Operand Stack underflow in instruction local.set");
            var localValue = context.Frame.Locals[localIndex];
            var type = localValue.Type;
            //4.
            var value = context.OpStack.PopType(type);
            //5.
            context.Frame.Locals[localIndex] = value;
        }
        
        //0x22
        // @Spec 3.3.5.2. local.tee
        private static void ValidateLocalTee(WasmValidationContext context, LocalIdx localIndex)
        {
            context.Assert(context.Locals.Contains(localIndex),
                $"Instruction local.tee was invalid. Context Locals did not contain {localIndex}");
            var value = context.Locals[localIndex];
            context.OpStack.PushType(value.Type);
            context.OpStack.PopType(value.Type);
        }
        // @Spec 4.4.5.3. local.tee
        private static void ExecuteLocalTee(ExecContext context, LocalIdx localIndex)
        {
            //1.
            context.Assert(context.OpStack.HasValue,
                ()=>$"Operand Stack underflow in instruction local.tee");
            var localValue = context.Frame.Locals[localIndex];
            //2.
            var value = context.OpStack.PopType(localValue.Type);
            //3.
            context.OpStack.PushValue(value);
            //4.
            context.OpStack.PushValue(value);
            //5.
            ExecuteLocalSet(context, localIndex);
        }
    }
    
    public class GlobalVariableInst : InstructionBase
    {
        public override OpCode OpCode { get; }
        public GlobalIdx Index { get; internal set; }

        public delegate void ValidationDelegate(WasmValidationContext context, GlobalIdx index);
        public delegate void ExecuteDelegate(ExecContext context, GlobalIdx index);

        private ValidationDelegate _validate;
        private ExecuteDelegate _execute;
        public GlobalVariableInst(OpCode opCode, ExecuteDelegate execute, ValidationDelegate validate) => 
            (_execute, _validate) = (execute, validate);

        public override void Validate(WasmValidationContext context) => _validate(context, Index);
        public override void Execute(ExecContext context) => _execute(context, Index);
        
        public override IInstruction Parse(BinaryReader reader)
        {
            Index = (GlobalIdx)reader.ReadLeb128_u32();
            return this;
        }
        
        public static GlobalVariableInst CreateInstGlobalGet() => new GlobalVariableInst(OpCode.LocalGet, ExecuteGlobalGet, ValidateGlobalGet);
        public static GlobalVariableInst CreateInstGlobalSet() => new GlobalVariableInst(OpCode.LocalSet, ExecuteGlobalSet, ValidateGlobalSet);
        
        //0x23
        // @Spec 3.3.5.4. global.get
        private static void ValidateGlobalGet(WasmValidationContext context, GlobalIdx globalIndex)
        {
            context.Assert(context.Globals.Contains(globalIndex),
                $"Instruction global.get was invalid. Context Globals did not contain {globalIndex}");
            var globalType = context.Globals[globalIndex].Type;
            context.OpStack.PushType(globalType.ContentType);
        }
        // @Spec 4.4.5.4. global.get
        private static void ExecuteGlobalGet(ExecContext context, GlobalIdx globalIndex)
        {
            //2.
            context.Assert(context.Frame.Module.GlobalAddrs.Contains(globalIndex),
                ()=>"Runtime Globals did not contain address for {globalIndex} in global.get");
            //3.
            var a = context.Frame.Module.GlobalAddrs[globalIndex];
            //4.
            context.Assert(context.Store.Contains(a),
                ()=>$"Runtime Store did not contain Global at address {a} in global.get");
            //5.
            var glob = context.Store[a];
            //6.
            var val = glob.Value;
            //7.
            context.OpStack.PushValue(val);
        }
        
        //0x24
        // @Spec 3.3.5.5. global.set
        private static void ValidateGlobalSet(WasmValidationContext context, GlobalIdx globalIndex)
        {
            context.Assert(context.Globals.Contains(globalIndex),
                $"Instruction global.set was invalid. Context Globals did not contain {globalIndex}");
            var global = context.Globals[globalIndex];
            var mut = global.Type.Mutability;
            context.Assert(mut == Mutability.Mutable,
                $"Instruction global.set was invalid. Trying to set immutable global {globalIndex}");
            context.OpStack.PopType(global.Type.ContentType);

        }
        // @Spec 4.4.5.5. global.set
        private static void ExecuteGlobalSet(ExecContext context, GlobalIdx globalIndex)
        {
            //2.
            context.Assert(context.Frame.Module.GlobalAddrs.Contains(globalIndex),
                ()=>"Runtime Globals did not contain address for {globalIndex} in global.set");
            //3.
            var a = context.Frame.Module.GlobalAddrs[globalIndex];
            //4.
            context.Assert(context.Store.Contains(a),
                ()=>$"Runtime Store did not contain Global at address {a} in global.set");
            //5.
            var glob = context.Store[a];
            //6.
            context.Assert(context.OpStack.HasValue,
                ()=>$"Operand Stack underflow in global.set");
            //7.
            var val = context.OpStack.PopType(glob.Type.ContentType);
            //8.
            glob.Value = val;

        }
        
    }
    
}