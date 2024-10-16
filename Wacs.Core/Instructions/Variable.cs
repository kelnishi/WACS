using System;
using System.IO;
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
        
        public delegate void ExecuteDelegate(IExecContext context, LocalIdx index);
        private ExecuteDelegate _execute;
        public LocalVariableInst(OpCode opCode, ExecuteDelegate execute) => _execute = execute;
        
        public override void Execute(IExecContext context) => _execute(context, Index);
        
        public override IInstruction Parse(BinaryReader reader)
        {
            Index = (LocalIdx)reader.ReadLeb128_u32();
            return this;
        }

        public static LocalVariableInst CreateInstLocalGet() => new LocalVariableInst(OpCode.LocalGet, ExecuteLocalGet);
        public static LocalVariableInst CreateInstLocalSet() => new LocalVariableInst(OpCode.LocalSet, ExecuteLocalSet);
        public static LocalVariableInst CreateInstLocalTee() => new LocalVariableInst(OpCode.LocalTee, ExecuteLocalTee);
        
        
        //0x20
        // @Spec 4.4.5.1. local.get 
        private static void ExecuteLocalGet(IExecContext context, LocalIdx localIndex)
        {
            // @Spec 3.3.5.1. local.get
            context.ValidateContext((ctx) => {
                if (!ctx.ExecContext.Locals.Contains(localIndex))
                    throw new InvalidProgramException($"Locals did not contain index {localIndex}");
            });
            var value = context.Locals[localIndex];
            context.OpStack.PushValue(value);
        }
        
        //0x21
        // @Spec 4.4.5.2. local.set
        private static void ExecuteLocalSet(IExecContext context, LocalIdx localIndex) 
        {
            // @Spec 3.3.5.2. local.set
            context.ValidateContext((ctx) => {
                if (!ctx.ExecContext.Locals.Contains(localIndex))
                    throw new InvalidProgramException($"Locals did not contain index {localIndex}");
            });

            var value = context.OpStack.PopAny();
            context.Locals[localIndex] = value;
        }
        
        //0x22
        // @Spec 4.4.5.3. local.tee
        private static void ExecuteLocalTee(IExecContext context, LocalIdx localIndex)
        {
            // @Spec 3.3.5.3. local.tee
            context.ValidateContext((ctx) => {
                if (!ctx.ExecContext.Locals.Contains(localIndex))
                    throw new InvalidProgramException($"Locals did not contain index {localIndex}");
            });
            var value = context.OpStack.PopAny();
            context.OpStack.PushValue(value);
            context.OpStack.PushValue(value);
            ExecuteLocalSet(context, localIndex);
        }
    }
    
    public class GlobalVariableInst : InstructionBase
    {
        public override OpCode OpCode { get; }
        public GlobalIdx Index { get; internal set; }
        
        public delegate void ExecuteDelegate(IExecContext context, GlobalIdx index);
        private ExecuteDelegate _execute;
        public GlobalVariableInst(OpCode opCode, ExecuteDelegate execute) => _execute = execute;
        
        public override void Execute(IExecContext context) => _execute(context, Index);
        
        public override IInstruction Parse(BinaryReader reader)
        {
            Index = (GlobalIdx)reader.ReadLeb128_u32();
            return this;
        }
        
        public static GlobalVariableInst CreateInstGlobalGet() => new GlobalVariableInst(OpCode.LocalGet, ExecuteGlobalGet);
        public static GlobalVariableInst CreateInstGlobalSet() => new GlobalVariableInst(OpCode.LocalSet, ExecuteGlobalSet);
        
        //0x23
        // @Spec 4.4.5.4. global.get
        private static void ExecuteGlobalGet(IExecContext context, GlobalIdx globalIndex)
        {
            // @Spec 3.3.5.4. global.get
            context.ValidateContext((ctx) => {
                if (!ctx.ExecContext.Globals.Contains(globalIndex))
                    throw new InvalidProgramException($"Globals did not contain index {globalIndex}");
            });
            var value = context.Globals[globalIndex];
            context.OpStack.PushValue(value);
        }
        
        //0x24
        // @Spec 4.4.5.5. global.set
        private static void ExecuteGlobalSet(IExecContext context, GlobalIdx globalIndex)
        {
            // @Spec 3.3.5.5. global.set
            context.ValidateContext((ctx) => {
                if (!ctx.ExecContext.Globals.Contains(globalIndex))
                    throw new InvalidProgramException($"Globals did not contain index {globalIndex}");
            });
            var value = context.OpStack.PopAny();
            context.Globals[globalIndex] = value;
        }
        
    }
    
}