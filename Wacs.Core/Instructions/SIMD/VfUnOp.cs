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

using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst 
    {
        public static readonly NumericInst F32x4Abs     = new (SimdCode.F32x4Abs     , ExecuteF32x4Abs    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Abs     = new (SimdCode.F64x2Abs     , ExecuteF64x2Abs    , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst F32x4Neg     = new (SimdCode.F32x4Neg     , ExecuteF32x4Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Neg     = new (SimdCode.F64x2Neg     , ExecuteF64x2Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst F32x4Sqrt    = new (SimdCode.F32x4Sqrt    , ExecuteF32x4Sqrt   , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Sqrt    = new (SimdCode.F64x2Sqrt    , ExecuteF64x2Sqrt   , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst F32x4Ceil    = new (SimdCode.F32x4Ceil    , ExecuteF32x4Ceil   , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Ceil    = new (SimdCode.F64x2Ceil    , ExecuteF64x2Ceil   , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst F32x4Floor   = new (SimdCode.F32x4Floor   , ExecuteF32x4Floor  , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Floor   = new (SimdCode.F64x2Floor   , ExecuteF64x2Floor  , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst F32x4Trunc   = new (SimdCode.F32x4Trunc   , ExecuteF32x4Trunc  , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Trunc   = new (SimdCode.F64x2Trunc   , ExecuteF64x2Trunc  , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst F32x4Nearest = new (SimdCode.F32x4Nearest , ExecuteF32x4Nearest, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Nearest = new (SimdCode.F64x2Nearest , ExecuteF64x2Nearest, ValidateOperands(pop: ValType.V128, push: ValType.V128));


        private static void ExecuteF32x4Abs(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Abs(val.F32x4_0),
                Math.Abs(val.F32x4_1),
                Math.Abs(val.F32x4_2),
                Math.Abs(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Abs(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Abs(val.F64x2_0),
                Math.Abs(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Neg(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                -val.F32x4_0,
                -val.F32x4_1,
                -val.F32x4_2,
                -val.F32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Neg(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                -val.F64x2_0,
                -val.F64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Sqrt(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)Math.Sqrt(val.F32x4_0),
                (float)Math.Sqrt(val.F32x4_1),
                (float)Math.Sqrt(val.F32x4_2),
                (float)Math.Sqrt(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Sqrt(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Sqrt(val.F64x2_0),
                Math.Sqrt(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Ceil(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)Math.Ceiling(val.F32x4_0),
                (float)Math.Ceiling(val.F32x4_1),
                (float)Math.Ceiling(val.F32x4_2),
                (float)Math.Ceiling(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Ceil(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Ceiling(val.F64x2_0),
                Math.Ceiling(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Floor(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)Math.Floor(val.F32x4_0),
                (float)Math.Floor(val.F32x4_1),
                (float)Math.Floor(val.F32x4_2),
                (float)Math.Floor(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Floor(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Floor(val.F64x2_0),
                Math.Floor(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Trunc(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)Math.Truncate(val.F32x4_0),
                (float)Math.Truncate(val.F32x4_1),
                (float)Math.Truncate(val.F32x4_2),
                (float)Math.Truncate(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Trunc(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Truncate(val.F64x2_0),
                Math.Truncate(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Nearest(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)Math.Round(val.F32x4_0),
                (float)Math.Round(val.F32x4_1),
                (float)Math.Round(val.F32x4_2),
                (float)Math.Round(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Nearest(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Round(val.F64x2_0),
                Math.Round(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }
    }
}