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

using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // @Spec 3.3.1.2. i.unop
        public static readonly NumericInst I32Clz    = new(OpCode.I32Clz    , ExecuteI32Clz    , ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Ctz    = new(OpCode.I32Ctz    , ExecuteI32Ctz    , ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Popcnt = new(OpCode.I32Popcnt , ExecuteI32Popcnt , ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I64Clz    = new(OpCode.I64Clz    , ExecuteI64Clz    , ValidateOperands(pop: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Ctz    = new(OpCode.I64Ctz    , ExecuteI64Ctz    , ValidateOperands(pop: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Popcnt = new(OpCode.I64Popcnt , ExecuteI64Popcnt , ValidateOperands(pop: ValType.I64, push: ValType.I64));

        // @Spec 4.3.2.20 iclz
        private static void ExecuteI32Clz(ExecContext context)
        {
            uint x = context.OpStack.PopU32();
            if (x != 0)
            {
                int count = 0;
                // Shift bits to the left until the most significant bit is 1
                while ((x & 0x80000000) == 0)
                {
                    x <<= 1;
                    count++;
                }
                context.OpStack.PushI32(count);
            }
            // Handle the case when x is 0 (all bits are 0)
            else
            {
                context.OpStack.PushI32(32);
            }
        }

        // @Spec 4.3.2.21 ictz
        private static void ExecuteI32Ctz(ExecContext context)
        {
            uint x = context.OpStack.PopU32();
            if (x != 0)
            {
                int count = 0;
                // Check each bit from the least significant bit (rightmost) upwards
                while ((x & 1) == 0)
                {
                    x >>= 1;
                    count++;
                }

                context.OpStack.PushI32(count);
            }
            // Handle the case when x is 0 (all bits are 0)
            else
            {
                context.OpStack.PushI32(32);
                return;
            }
        }

        // @Spec 4.3.2.22 ipopcnt
        private static void ExecuteI32Popcnt(ExecContext context)
        {
            uint x = context.OpStack.PopU32();
            uint count = 0;
            while (x != 0)
            {
                count += x & 1;  // Add 1 to count if the least significant bit is 1
                x >>= 1;         // Right shift x by 1 to process the next bit
            }
            context.OpStack.PushU32(count);
        }

        private static void ExecuteI64Clz(ExecContext context)
        {
            ulong x = context.OpStack.PopU64();
            if (x != 0)
            {
                int count = 0;
                while ((x & 0x8000000000000000) == 0)
                {
                    x <<= 1;
                    count++;
                }
                context.OpStack.PushI64(count);
            }
            else
            {
                context.OpStack.PushI64(64);
            }
        }

        private static void ExecuteI64Ctz(ExecContext context)
        {
            ulong x = context.OpStack.PopU64();
            if (x != 0)
            {
                int count = 0;
                while ((x & 1) == 0)
                {
                    x >>= 1;
                    count++;
                }
                context.OpStack.PushI64(count);
            }
            else
            {
                context.OpStack.PushI64(64);
            }
        }

        private static void ExecuteI64Popcnt(ExecContext context)
        {
            ulong x = context.OpStack.PopU64();
            ulong count = 0;
            while (x != 0)
            {
                count += x & 1;  // Add 1 to count if the least significant bit is 1
                x >>= 1;         // Right shift x by 1 to process the next bit
            }
            context.OpStack.PushU64(count);
            
        }
    }
}