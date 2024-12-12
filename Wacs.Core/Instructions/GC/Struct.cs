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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.GC;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.GC
{
    public class InstStructNew : InstructionBase, IConstInstruction
    {
        private TypeIdx X;
        public override ByteCode Op => GcCode.StructNew;

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-structmathsfstructnewx
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the Context.",Op.GetMnemonic(), X);

            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var structType = compositeType as StructType;
            context.Assert(structType,
                "Instruction {0} was invalid. Referenced Type was not struct:{1}",Op.GetMnemonic(),compositeType);

            foreach (var ft in structType.FieldTypes.Reverse())
            {
                context.OpStack.PopType(ft.UnpackType());
            }

            var resultType = ValType.Ref | (ValType)X;
            context.OpStack.PushType(resultType);
        }

        public override void Execute(ExecContext context)
        {
            //2
            context.Assert(context.Frame.Module.Types.Contains(X),
                $"Instruction {Op.GetMnemonic()} failed. Context did not contain type {X.Value}.");
            //3
            var defType = context.Frame.Module.Types[X];
            //4
            var structFt = defType.Expansion as StructType;
            //5
            context.Assert(structFt,
                $"Instruction {Op.GetMnemonic()} failed. Defined Type was not a struct {defType}.");
            //6
            int n = structFt.FieldTypes.Length;
            //7
            context.Assert(context.OpStack.Count >= n,
                $"Instruction {Op.GetMnemonic()} failed. Operand stack underflow.");
            //8
            Stack<Value> vals = new();
            context.OpStack.PopResults(n, ref vals);
            
            //9,10,11,12,13
            var a = context.Store.AddStruct();
            var si = new StoreStruct(a, structFt, vals);

            //14
            //*We're relying on the C# Runtime's heap to manage this ref.
            // the Value here holds the reference and will get cleaned up when copies leave the stack.
            var refStruct = new Value(ValType.Ref | (ValType)X, si);
            context.OpStack.PushValue(refStruct);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstStructNewDefault : InstructionBase, IConstInstruction
    {
        private TypeIdx X;
        public override ByteCode Op => GcCode.StructNewDefault;

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-structmathsfstructnewx
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the Context.",Op.GetMnemonic(), X);

            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var structType = compositeType as StructType;
            context.Assert(structType,
                "Instruction {0} was invalid. Referenced Type was not struct:{1}",Op.GetMnemonic(),compositeType);

            foreach (var ft in structType.FieldTypes.Reverse())
            {
                context.Assert(ft.StorageType.IsDefaultable(),
                    "Instruction {0} was invalid. FieldType was not defaultable:{1}",Op.GetMnemonic(),ft);
            }
            var resultType = ValType.Ref | (ValType)X;
            context.OpStack.PushType(resultType);
        }

        public override void Execute(ExecContext context)
        {
            //2
            context.Assert(context.Frame.Module.Types.Contains(X),
                $"Instruction {Op.GetMnemonic()} failed. Context did not contain type {X.Value}.");
            //3
            var defType = context.Frame.Module.Types[X];
            //4
            var structFt = defType.Expansion as StructType;
            //5
            context.Assert(structFt,
                $"Instruction {Op.GetMnemonic()} failed. Defined Type was not a struct {defType}.");
            //6,7,8
            //Skip the stack, just construct the default struct            
            //9,10,11,12,13
            var a = context.Store.AddStruct();
            var si = new StoreStruct(a, structFt);

            //14
            //*We're relying on the C# Runtime's heap to manage this ref.
            // the Value here holds the reference and will get cleaned up when copies leave the stack.
            var refStruct = new Value(ValType.Ref | (ValType)X, si);
            context.OpStack.PushValue(refStruct);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstStructGet : InstructionBase
    {
        private PackedExt Sx;
        private TypeIdx X;
        private FieldIdx Y;

        public override ByteCode Op => Sx switch
        {
            PackedExt.Signed => GcCode.StructGetS,
            PackedExt.NotPacked => GcCode.StructGet,
            PackedExt.Unsigned => GcCode.StructGetU,
            _ => throw new InvalidDataException($"Undefined packedtype: {Sx}")
        };

        public InstStructGet(PackedExt sx)
        {
            Sx = sx;
        }
        
        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-structmathsfstructgetmathsf_hrefsyntax-sxmathitsxxy
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the Context.",Op.GetMnemonic(), X);
            
            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var structType = compositeType as StructType;
            context.Assert(structType,
                "Instruction {0} was invalid. Referenced Type was not struct:{1}",Op.GetMnemonic(),compositeType);
            context.Assert(structType.FieldTypes.Length > Y.Value,
                "Instruction {0} was invalid. Fields did not include index:{1}",Op.GetMnemonic(),Y.Value);
            
            var fieldtype = structType[Y];
            var t = fieldtype.UnpackType();
            
            context.Assert(fieldtype.ValidExtension(Sx),
                "Instruction {0} was invalid. Bad packing extension:{1}",Op.GetMnemonic(), Sx);
            
            var refType = ValType.NullableRef | (ValType)X;
            context.OpStack.PopType(refType);
            context.OpStack.PushType(t);
        }

        public override void Execute(ExecContext context)
        {
            //2
            context.Assert(context.Frame.Module.Types.Contains(X),
                $"Instruction {Op.GetMnemonic()} failed. DefType {X} was not in the context");
            //3
            var defType = context.Frame.Module.Types[X];
            var compositeType = defType.Expansion;
            //4, 5
            var structType = compositeType as StructType;
            context.Assert(structType,
                $"Instruction {Op.GetMnemonic()} was invalid. Referenced Type was not struct:{compositeType}");
            context.Assert(structType.FieldTypes.Length > Y.Value,
                $"Instruction {Op.GetMnemonic()} was invalid. Fields did not include index:{Y.Value}");
            //6
            var fty = structType[Y];
            //7
            var refType = ValType.NullableRef | (ValType)X;
            context.Assert( context.OpStack.Peek().IsRef(refType),
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //8
            var refVal = context.OpStack.PopType(refType);
            //9
            if (refVal.IsNullRef)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Ref was null");
            //10,11
            var refStruct = refVal.GcRef as StoreStruct;
            context.Assert(refStruct,
                $"Instruction {Op.GetMnemonic()} failed. Ref was not a struct.");
            //12
            context.Assert(context.Store.Contains(refStruct.StructIndex),
                $"Instruction {Op.GetMnemonic()} failed. Store did not contain reference {refStruct.StructIndex.Value}");
            //13,14
            var fieldVal = refStruct[Y];
            if (Sx != PackedExt.NotPacked)
            {
                switch (fty.StorageType)
                {
                    case ValType.I8 when Sx == PackedExt.Signed:
                        fieldVal.Data.Int32 = (sbyte)fieldVal.Data.Int32; break;
                    case ValType.I8 when Sx == PackedExt.Unsigned:
                        fieldVal.Data.UInt32 = (byte)fieldVal.Data.UInt32; break;
                    case ValType.I16 when Sx == PackedExt.Signed:
                        fieldVal.Data.Int32 = (short)fieldVal.Data.Int32; break;
                    case ValType.I16 when Sx == PackedExt.Unsigned:
                        fieldVal.Data.UInt32 = (ushort)fieldVal.Data.UInt32; break;
                }
            }
            //15
            context.OpStack.PushValue(fieldVal);
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            Y = (FieldIdx)reader.ReadLeb128_u32();
            return this;
        }
    }

    public class InstStructSet : InstructionBase
    {
        private TypeIdx X;
        private FieldIdx Y;
        
        public override ByteCode Op => GcCode.StructSet;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the Context.",Op.GetMnemonic(), X);
            
            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var structType = compositeType as StructType;
            context.Assert(structType,
                "Instruction {0} was invalid. Referenced Type was not struct:{1}",Op.GetMnemonic(),compositeType);
            context.Assert(structType.FieldTypes.Length > Y.Value,
                "Instruction {0} was invalid. Fields did not include index:{1}",Op.GetMnemonic(),Y.Value);
            
            var fieldtype = structType[Y];
            context.Assert(fieldtype.Mut == Mutability.Mutable,
                "Instruction {0} was invalid. Cannot set immutable field:{1}",Op.GetMnemonic(),Y.Value);
            
            var t = fieldtype.UnpackType();
            var refType = ValType.NullableRef | (ValType)X;

            context.OpStack.PopType(t);
            context.OpStack.PopType(refType);
        }

        public override void Execute(ExecContext context)
        {
            //2
            context.Assert(context.Frame.Module.Types.Contains(X),
                $"Instruction {Op.GetMnemonic()} failed. DefType {X} was not in the context");
            //3
            var defType = context.Frame.Module.Types[X];
            var compositeType = defType.Expansion;
            //4, 5
            var structType = compositeType as StructType;
            context.Assert(structType,
                $"Instruction {Op.GetMnemonic()} was invalid. Referenced Type was not struct:{compositeType}");
            context.Assert(structType.FieldTypes.Length > Y.Value,
                $"Instruction {Op.GetMnemonic()} was invalid. Fields did not include index:{Y.Value}");
            //6
            var fty = structType[Y];
            //7
            context.Assert( context.OpStack.Peek().IsType(fty.StorageType),
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //8
            var val = context.OpStack.PopType(fty.StorageType);
            //9
            var refType = ValType.NullableRef | (ValType)X;
            context.Assert( context.OpStack.Peek().IsRef(refType),
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //10
            var refVal = context.OpStack.PopType(refType);
            //11
            if (refVal.IsNullRef)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Ref was null");
            //12,13
            var refStruct = refVal.GcRef as StoreStruct;
            context.Assert(refStruct,
                $"Instruction {Op.GetMnemonic()} failed. Ref was not a struct.");
            //14
            context.Assert(context.Store.Contains(refStruct.StructIndex),
                $"Instruction {Op.GetMnemonic()} failed. Store did not contain reference {refStruct.StructIndex.Value}");
            
            //15
            //16
            refStruct[Y] = val;
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            Y = (FieldIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
}