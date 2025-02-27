// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    //0x41
    public sealed class InstI32Const : InstructionBase, IConstInstruction, ITypedValueProducer<int>
    {
        public static InstI32Const Inst = new();
        
        public InstI32Const() : base(ByteCode.I32Const, +1) { }
        
        public int Value;
        public Func<ExecContext, int> GetFunc => FetchImmediate;
        public int CalculateSize() => 1;

        public int LinkStackDiff => StackDiff;

        /// <summary>
        /// @Spec 3.3.1.1 t.const
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context) =>
            context.OpStack.PushI32(Value);

        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context)
        {
            context.OpStack.PushI32(Value);
        }

        private static readonly Dictionary<int, InstI32Const> LookupCache = new();
        
        public override InstructionBase Parse(BinaryReader reader) {
            return Immediate(reader.ReadLeb128_s32());
        }

        public InstructionBase Immediate(int value)
        {
            if (LookupCache.TryGetValue(value, out var get))
                return get;

            var inst = new InstI32Const {
                Value = value
            };
            LookupCache.Add(value, inst);
            return inst;
        }

        public int FetchImmediate(ExecContext _) => Value;
        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {Value}";
    }
    
    //0x42
    public sealed class InstI64Const : InstructionBase, IConstInstruction, ITypedValueProducer<long>
    {
        public InstI64Const() : base(ByteCode.I64Const, +1) { }
        public int LinkStackDiff => StackDiff;
        
        private long Value;
        public Func<ExecContext, long> GetFunc => FetchImmediate;
        public int CalculateSize() => 1;

        /// <summary>
        /// @Spec 3.3.1.1 t.const
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context) =>
            context.OpStack.PushI64(Value);

        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context)
        {
            context.OpStack.PushI64(Value);
        }

        public override InstructionBase Parse(BinaryReader reader) {
            Value = reader.ReadLeb128_s64();
            return this;
        }

        public long FetchImmediate(ExecContext _) => Value;

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {Value}";
    }
    
    //0x43
    public sealed class InstF32Const : InstructionBase, IConstInstruction, ITypedValueProducer<float>
    {
        public InstF32Const() : base(ByteCode.F32Const, +1) { }
        public int LinkStackDiff => StackDiff;
        
        private float Value;
        public Func<ExecContext, float> GetFunc => FetchImmediate;
        public int CalculateSize() => 1;

        /// <summary>
        /// @Spec 3.3.1.1 t.const
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context) =>
            context.OpStack.PushF32(Value);

        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context)
        {
            context.OpStack.PushF32(Value);
        }

        public override InstructionBase Parse(BinaryReader reader) {
            Value = reader.Read_f32();
            return this;
        }

        public float FetchImmediate(ExecContext _) => Value;

        public override string RenderText(ExecContext? context)
        {
            var sourceText = Value.ToString(CultureInfo.InvariantCulture).ToLower();
            if (sourceText.Contains("e-") || sourceText.Contains("e+") || sourceText.Length > 5)
                sourceText = Value.ToString("0.#####e+00");
            var floatText = FloatFormatter.FormatFloat(Value);
            return
                $"{base.RenderText(context)} {floatText} (;={sourceText};)";
        }
    }
    
    //0x44
    public sealed class InstF64Const : InstructionBase, IConstInstruction, ITypedValueProducer<double>
    {
        public InstF64Const() : base(ByteCode.F64Const, +1) { }
        public int LinkStackDiff => StackDiff;
        
        private double Value;
        public Func<ExecContext, double> GetFunc => FetchImmediate;
        public int CalculateSize() => 1;

        /// <summary>
        /// @Spec 3.3.1.1 t.const
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context) =>
            context.OpStack.PushF64(Value);

        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context)
        {
            context.OpStack.PushF64(Value);
        }

        public override InstructionBase Parse(BinaryReader reader) {
            Value = reader.Read_f64();
            return this;
        }

        public double FetchImmediate(ExecContext _) => Value;

        public override string RenderText(ExecContext? context)
        {
            var sourceText = Value.ToString(CultureInfo.InvariantCulture).ToLower();
            if (sourceText.Contains("e-") || sourceText.Contains("e+") || sourceText.Length > 5)
                sourceText = Value.ToString("0.#####e+00");
            var doubleText = FloatFormatter.FormatDouble(Value);
            return
                $"{base.RenderText(context)} {doubleText} (;={sourceText};)";
        }
    }
}