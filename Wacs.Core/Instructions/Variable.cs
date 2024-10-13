using System;
using System.IO;
using Wacs.Core.Execution;
using Wacs.Core.OpCodes;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// 5.4.4 Variable Instructions
namespace Wacs.Core.Instructions
{
    public class VariableInst : InstructionBase
    {
        public override OpCode OpCode { get; }
        public uint Index { get; internal set; }
        
        public delegate void ExecuteDelegate(ExecContext context, uint index);
        private ExecuteDelegate _execute;
        public VariableInst(OpCode opCode, ExecuteDelegate execute) => _execute = execute;
        
        public override void Execute(ExecContext context) => _execute(context, Index);
        
        public override IInstruction Parse(BinaryReader reader)
        {
            Index = reader.ReadLeb128_u32();
            return this;
        }

        public static VariableInst CreateInstLocalGet() => new VariableInst(OpCode.LocalGet, ExecuteLocalGet);
        public static VariableInst CreateInstLocalSet() => new VariableInst(OpCode.LocalSet, ExecuteLocalSet);
        public static VariableInst CreateInstLocalTee() => new VariableInst(OpCode.LocalTee, ExecuteLocalTee);
        public static VariableInst CreateInstGlobalGet() => new VariableInst(OpCode.LocalGet, ExecuteGlobalGet);
        public static VariableInst CreateInstGlobalSet() => new VariableInst(OpCode.LocalSet, ExecuteGlobalSet);

        
        
        //0x20
        // @Spec 3.3.5.1. local.get
        // @Spec 4.4.5.1. local.get 
        private static void ExecuteLocalGet(ExecContext context, uint localIndex)
        {
            //TODO: Use current frame
            context.ValidateContext((ctx) => {
                if (localIndex >= ctx.Locals.Count)
                    throw new InvalidProgramException($"Locals did not contain index {localIndex}");
            });
            var value = context.GetLocal(localIndex);
            context.Stack.PushValue(value);
        }
        
        //0x21
        // @Spec 3.3.5.2. local.set
        // @Spec 4.4.5.2. local.set
        private static void ExecuteLocalSet(ExecContext context, uint localIndex) {
            context.ValidateContext((ctx) => {
                if (localIndex >= ctx.Locals.Count)
                    throw new InvalidProgramException($"Locals did not contain index {localIndex}");
            });

            StackValue value = context.Stack.PopAny();
            context.SetLocal(localIndex, value);
        }
        
        //0x22
        // @Spec 3.3.5.3. local.tee
        // @Spec 4.4.5.3. local.tee
        private static void ExecuteLocalTee(ExecContext context, uint localIndex)
        {
            var value = context.Stack.PopAny();
            context.Stack.PushValue(value);
            context.Stack.PushValue(value);
            ExecuteLocalSet(context, localIndex);
        }
        
        //0x23
        // @Spec 3.3.5.4. global.get
        // @Spec 4.4.5.4. global.get
        private static void ExecuteGlobalGet(ExecContext context, uint globalIndex)
        {
            context.ValidateContext((ctx) => {
                //TODO check current frame
                if (globalIndex >= ctx.Globals.Count)
                    throw new InvalidProgramException($"Globals did not contain index {globalIndex}");
                
                //TODO check current store
                // ???
                
                //TODO: If inside a global, element or data expression, only imported globals are valid.
            });

            var g = context.GetGlobal(globalIndex);
            context.Stack.PushValue(g);
        }
        
        //0x24
        // @Spec 3.3.5.5. global.set
        // @Spec 4.4.5.5. global.set
        private static void ExecuteGlobalSet(ExecContext context, uint globalIndex)
        {
            context.ValidateContext((ctx) => {
                if (globalIndex >= ctx.Globals.Count)
                    throw new InvalidProgramException($"Globals did not contain index {globalIndex}");
            });

            StackValue value = context.Stack.PopAny();
            context.SetGlobal(globalIndex, value);
        }
        
    }
    
}