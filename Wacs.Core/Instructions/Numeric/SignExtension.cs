using System;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst 
    {
        public static readonly NumericInst I32Extend8S  = new(OpCode.I32Extend8S , ExecuteI32Extend8S , ValidateOperands(pop: ValType.I32, push: ValType.I32));  
        public static readonly NumericInst I32Extend16S = new(OpCode.I32Extend16S, ExecuteI32Extend16S, ValidateOperands(pop: ValType.I32, push: ValType.I32)); 
        public static readonly NumericInst I64Extend8S  = new(OpCode.I64Extend8S , ExecuteI64Extend8S , ValidateOperands(pop: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Extend16S = new(OpCode.I64Extend16S, ExecuteI64Extend16S, ValidateOperands(pop: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Extend32S = new(OpCode.I64Extend32S, ExecuteI64Extend32S, ValidateOperands(pop: ValType.I64, push: ValType.I64));

        private const UInt32 ByteSign = 0x80;
        private const UInt32 I32ByteExtend = 0xFFFF_FF80;
        private const UInt32 ByteMask = 0xFF;
        
        private static void ExecuteI32Extend8S(ExecContext context)
        {
            uint value = context.OpStack.PopI32();
            uint result = ((value & ByteSign) != 0) 
                ? (I32ByteExtend | value) 
                : (ByteMask & value);
            context.OpStack.PushI32((int)result);
        }

        private const UInt32 ShortSign = 0x8000;
        private const UInt32 I32ShortExtend = 0xFFFF_8000;
        private const UInt32 ShortMask = 0xFFFF;
        
        private static void ExecuteI32Extend16S(ExecContext context)
        {
            uint value = context.OpStack.PopI32();
            uint result = ((value & ShortSign) != 0) 
                ? (I32ShortExtend | value) 
                : (ShortMask & value);
            context.OpStack.PushI32((int)result);
        }
        
        private const UInt64 I64ByteExtend = 0xFFFF_FFFF_FFFF_FF80;

        private static void ExecuteI64Extend8S(ExecContext context)
        {
            ulong value = context.OpStack.PopI64();
            ulong result = ((value & ByteSign) != 0) 
                ? (I64ByteExtend | value) 
                : (ByteMask & value);
            context.OpStack.PushI64((long)result);
        }
        
        private const UInt64 I64ShortExtend = 0xFFFF_FFFF_FFFF_8000;

        private static void ExecuteI64Extend16S(ExecContext context)
        {
            ulong value = context.OpStack.PopI64();
            ulong result = ((value & ShortSign) != 0) 
                ? (I64ShortExtend | value) 
                : (ShortMask & value);
            context.OpStack.PushI64((long)result);
        }

        private const UInt64 WordSign = 0x8000_0000;
        private const UInt64 WordExtend = 0xFFFF_FFFF_8000_0000;
        private const UInt64 WordMask = 0xFFFF_FFFF;
        
        private static void ExecuteI64Extend32S(ExecContext context)
        {
            ulong value = context.OpStack.PopI64();
            ulong result = ((value & WordSign) != 0) 
                ? (WordExtend | value) 
                : (WordMask & value);
            context.OpStack.PushI64((long)result);
        }
    }
}