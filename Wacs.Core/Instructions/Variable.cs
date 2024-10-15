using System;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
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
        
        public delegate void ExecuteDelegate(ExecContext context, LocalIdx index);
        private ExecuteDelegate _execute;
        public LocalVariableInst(OpCode opCode, ExecuteDelegate execute) => _execute = execute;
        
        public override void Execute(ExecContext context) => _execute(context, Index);
        
        public override IInstruction Parse(BinaryReader reader)
        {
            Index = (LocalIdx)reader.ReadLeb128_u32();
            return this;
        }

        public static LocalVariableInst CreateInstLocalGet() => new LocalVariableInst(OpCode.LocalGet, ExecuteLocalGet);
        public static LocalVariableInst CreateInstLocalSet() => new LocalVariableInst(OpCode.LocalSet, ExecuteLocalSet);
        public static LocalVariableInst CreateInstLocalTee() => new LocalVariableInst(OpCode.LocalTee, ExecuteLocalTee);
        
        
        //0x20
        // @Spec 3.3.5.1. local.get
        // @Spec 4.4.5.1. local.get 
        private static void ExecuteLocalGet(ExecContext context, LocalIdx localIndex)
        {
            //TODO: Use current frame
            context.ValidateContext((ctx) => {
                // if (!ctx.Locals.Contains(localIndex))
                //     throw new InvalidProgramException($"Locals did not contain index {localIndex}");
            });
            var value = context.GetLocal(localIndex);
            context.OpStack.PushValue(value);
        }
        
        //0x21
        // @Spec 3.3.5.2. local.set
        // @Spec 4.4.5.2. local.set
        private static void ExecuteLocalSet(ExecContext context, LocalIdx localIndex) {
            context.ValidateContext((ctx) => {
                // if (!ctx.Locals.Contains(localIndex))
                //     throw new InvalidProgramException($"Locals did not contain index {localIndex}");
            });

            Value value = context.OpStack.PopAny();
            context.SetLocal(localIndex, value);
        }
        
        //0x22
        // @Spec 3.3.5.3. local.tee
        // @Spec 4.4.5.3. local.tee
        private static void ExecuteLocalTee(ExecContext context, LocalIdx localIndex)
        {
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
        
        public delegate void ExecuteDelegate(ExecContext context, GlobalIdx index);
        private ExecuteDelegate _execute;
        public GlobalVariableInst(OpCode opCode, ExecuteDelegate execute) => _execute = execute;
        
        public override void Execute(ExecContext context) => _execute(context, Index);
        
        public override IInstruction Parse(BinaryReader reader)
        {
            Index = (GlobalIdx)reader.ReadLeb128_u32();
            return this;
        }
        
        public static GlobalVariableInst CreateInstGlobalGet() => new GlobalVariableInst(OpCode.LocalGet, ExecuteGlobalGet);
        public static GlobalVariableInst CreateInstGlobalSet() => new GlobalVariableInst(OpCode.LocalSet, ExecuteGlobalSet);
        
        //0x23
        // @Spec 3.3.5.4. global.get
        // @Spec 4.4.5.4. global.get
        private static void ExecuteGlobalGet(ExecContext context, GlobalIdx globalIndex)
        {
            context.ValidateContext((ctx) => {
                //TODO check current frame
                if (!ctx.Globals.Contains(globalIndex))
                    throw new InvalidProgramException($"Globals did not contain index {globalIndex}");
                
                //TODO check current store
                // ???
                
                //TODO: If inside a global, element or data expression, only imported globals are valid.
            });

            var g = context.GetGlobal(globalIndex);
            context.OpStack.PushValue(g);
        }
        
        //0x24
        // @Spec 3.3.5.5. global.set
        // @Spec 4.4.5.5. global.set
        private static void ExecuteGlobalSet(ExecContext context, GlobalIdx globalIndex)
        {
            context.ValidateContext((ctx) => {
                if (!ctx.Globals.Contains(globalIndex))
                    throw new InvalidProgramException($"Globals did not contain index {globalIndex}");
            });

            Value value = context.OpStack.PopAny();
            context.SetGlobal(globalIndex, value);
        }
        
    }
    
}