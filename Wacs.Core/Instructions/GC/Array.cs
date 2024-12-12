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
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.GC
{
    public class InstArrayNew : InstructionBase, IConstInstruction
    {
        private TypeIdx X;
        public override ByteCode Op => GcCode.ArrayNew;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the Context.",Op.GetMnemonic(), X);

            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                "Instruction {0} was invalid. Referenced Type was not array:{1}",Op.GetMnemonic(),compositeType);

            var t = arrayType.ElementType.UnpackType();

            context.OpStack.PopI32();
            context.OpStack.PopType(t);
            
            var resultType = ValType.Ref | (ValType)X;
            context.OpStack.PushType(resultType);
        }

        public override void Execute(ExecContext context)
        {
            //1
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type at top of stack {context.OpStack.Peek().Type}.");
            //2
            int n = context.OpStack.PopI32();
            //3
            context.Assert(context.OpStack.Count > 0,
                $"Instruction {Op.GetMnemonic()} failed. Operand stack underflow.");
            //4
            var val = context.OpStack.PopAny();
            
            //5,6 array.new_fixed inline...
            
            //2
            context.Assert(context.Frame.Module.Types.Contains(X),
                $"Instruction {Op.GetMnemonic()} failed. Context did not contain type {X.Value}.");
            //3  
            var defType = context.Frame.Module.Types[X];
            //5
            var arrayType = defType.Expansion as ArrayType;
            //4
            context.Assert(arrayType,
                $"Instruction {Op.GetMnemonic()} failed. Defined Type was not an array {defType}.");
            //6 skip the stack since we're inline
            //7,8,9 fan the values in the StoreArray constructor
            //11,12
            var a = context.Store.AddArray();
            
            //10
            var ai = new StoreArray(a, arrayType, val, n);
            
            //13
            var refArray = new Value(ValType.Ref | (ValType)X, ai);
            context.OpStack.PushValue(refArray);
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }
    }

    public class InstArrayNewDefault : InstructionBase, IConstInstruction
    {
        private TypeIdx X;
        public override ByteCode Op => GcCode.ArrayNew;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the Context.",Op.GetMnemonic(), X);

            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                "Instruction {0} was invalid. Referenced Type was not array:{1}",Op.GetMnemonic(),compositeType);

            var ft = arrayType.ElementType;
            
            context.Assert(ft.StorageType.IsDefaultable(),
                "Instruction {0} was invalid. FieldType was not defaultable:{1}",Op.GetMnemonic(),ft);
            
            context.OpStack.PopI32();
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
            //5
            var arrayType = defType.Expansion as ArrayType;
            //4
            context.Assert(arrayType,
                $"Instruction {Op.GetMnemonic()} failed. Defined Type was not an array {defType}.");
            //6
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type at top of stack {context.OpStack.Peek().Type}.");
            //7
            int n = context.OpStack.PopI32();
            //8,9
            //10,11 skip the stack since we're inline
            var a = context.Store.AddArray();
            var ai = new StoreArray(a, arrayType, n);
            var refArray = new Value(ValType.Ref | (ValType)X, ai);
            context.OpStack.PushValue(refArray);
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstArrayNewFixed : InstructionBase, IConstInstruction
    {
        private TypeIdx X;
        private uint N;
        public override ByteCode Op => GcCode.ArrayNew;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the Context.",Op.GetMnemonic(), X);

            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                "Instruction {0} was invalid. Referenced Type was not array:{1}",Op.GetMnemonic(),compositeType);

            var t = arrayType.ElementType.UnpackType();

            for (int i = 0; i < N; ++i)
            {
                context.OpStack.PopType(t);
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
            //5
            var arrayType = defType.Expansion as ArrayType;
            //4
            context.Assert(arrayType,
                $"Instruction {Op.GetMnemonic()} failed. Defined Type was not an array {defType}.");
            //7
            var values = new Stack<Value>();
            for (int i = 0; i < N; ++i)
            {
                values.Push(context.OpStack.PopAny());
            }
            //8,9 fan the values in the StoreArray constructor
            //11,12
            var a = context.Store.AddArray();
            //10
            var ai = new StoreArray(a, arrayType, ref values);
            //13
            var refArray = new Value(ValType.Ref | (ValType)X, ai);
            context.OpStack.PushValue(refArray);
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            N = reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstArrayNewData : InstructionBase
    {
        private TypeIdx X;
        private DataIdx Y;
        
        public override ByteCode Op => GcCode.ArrayNewData;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the Context.",Op.GetMnemonic(), X);

            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                "Instruction {0} was invalid. Referenced Type was not array:{1}",Op.GetMnemonic(),compositeType);

            var t = arrayType.ElementType.UnpackType();
            var ft = arrayType.ElementType;
            context.Assert(ft.BitWidth() != BitWidth.None,
                "Instruction {0} was invalid. Array field type does not have a bitwidth:{1}",Op.GetMnemonic(),ft);
            
            context.Assert(t.IsVal(),
                "Instruction {0} was invalid. Array data can only be numeric or vector:{1}",Op.GetMnemonic(),t);

            context.Assert(context.Datas.Contains(Y),
                "Instruction {0} was invalid. Data {1} was not in the Context.",Op.GetMnemonic(), Y);
            
            context.OpStack.PopI32();
            context.OpStack.PopI32();
            
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
            //5
            var arrayType = defType.Expansion as ArrayType;
            //4
            context.Assert(arrayType,
                $"Instruction {Op.GetMnemonic()} failed. Defined Type was not an array {defType}.");
            //6
            context.Assert(context.Frame.Module.DataAddrs.Contains(Y),
                $"Instruction {Op.GetMnemonic()} was invalid. Data {Y} address was not in the Context.");
            //7
            var da = context.Frame.Module.DataAddrs[Y];
            //8
            context.Assert(context.Store.Contains(da),
                $"Instruction {Op.GetMnemonic()} was invalid. Data &{da} was not in the Store.");
            //9
            var datainst = context.Store[da];
            //10,11,12
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type at top of stack {context.OpStack.Peek().Type}.");
            var n = context.OpStack.PopI32();
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type at top of stack {context.OpStack.Peek().Type}.");
            var s = context.OpStack.PopI32();
            //13
            var ft = arrayType.ElementType;
            context.Assert(ft.BitWidth() != BitWidth.None,
                $"Instruction {Op.GetMnemonic()} failed. FieldType does not have a valid bitwidth {ft}.");
            //14
            var z = ft.BitWidth().ByteSize();
            //15
            int start = s * z;
            int end = start + n * z;
            int span = end - z + 1;
            if (span > datainst.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Array size exceeds data length");
            //16
            //HACK: Unaligned read is some nonsense
            if (end > datainst.Data.Length)
            {
                end = datainst.Data.Length;
                start = end - n * z;
            }
            var b = datainst.Data.AsSpan()[start..end];
            //19 skip the stack since we're inline
            var a = context.Store.AddArray();
            //17,18
            var ai = new StoreArray(a, arrayType, b, n, z);
            var refArray = new Value(ValType.Ref | (ValType)X, ai);
            context.OpStack.PushValue(refArray);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            Y = (DataIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstArrayNewElem : InstructionBase
    {
        private TypeIdx X;
        private ElemIdx Y;
        
        public override ByteCode Op => GcCode.ArrayNewData;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the Context.",Op.GetMnemonic(), X);

            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                "Instruction {0} was invalid. Referenced Type was not array:{1}",Op.GetMnemonic(),compositeType);
            
            var rt = arrayType.ElementType.StorageType;
            context.Assert(rt.IsRefType(),
                "Instruction {0} was invalid. Array field type is not a RefType:{1}",Op.GetMnemonic(),rt);
            
            context.Assert(context.Elements.Contains(Y),
                "Instruction {0} was invalid. Element {1} was not in the Context.",Op.GetMnemonic(), Y);

            var rtp = context.Elements[Y].Type;
            
            context.Assert(rtp.Matches(rt, context.Types),
                "Instruction {0} was invalid. ElementType {1} does not match FieldType {2}",Op.GetMnemonic(), rtp, rt);
            
            context.OpStack.PopI32();
            context.OpStack.PopI32();
            
            var resultType = ValType.Ref | (ValType)X;
            context.OpStack.PushType(resultType);
        }

        public override void Execute(ExecContext context)
        {
            //2
            context.Assert(context.Frame.Module.ElemAddrs.Contains(Y),
                $"Instruction {Op.GetMnemonic()} failed. Context did not contain element {Y.Value}.");
            //3
            var ea = context.Frame.Module.ElemAddrs[Y];
            //4
            context.Assert(context.Store.Contains(ea),
                $"Instruction {Op.GetMnemonic()} failed. Store did not contain element &{ea}.");
            //5
            var eleminst = context.Store[ea];
            //6,7,8
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type at top of stack {context.OpStack.Peek().Type}.");
            var n = context.OpStack.PopI32();
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type at top of stack {context.OpStack.Peek().Type}.");
            var s = context.OpStack.PopI32();
            //9
            if (s + n > eleminst.Elements.Count)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Array size exceeds elements length");
            //10
            var refs = eleminst.Elements.Skip(s).Take(n).ToList();
            //11 skip the stack since we're inline
            var defType = context.Frame.Module.Types[X];
            var arrayType = defType.Expansion as ArrayType;
            context.Assert(arrayType,
                $"Instruction {Op.GetMnemonic()} failed. Defined Type was not an array {defType}.");
            
            var a = context.Store.AddArray();
            //17,18
            var ai = new StoreArray(a, arrayType, refs);
            var refArray = new Value(ValType.Ref | (ValType)X, ai);
            context.OpStack.PushValue(refArray);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            Y = (ElemIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstArrayGet : InstructionBase
    {
        private PackedExt Sx;
        private TypeIdx X;

        public override ByteCode Op => Sx switch
        {
            PackedExt.Signed => GcCode.ArrayGetS,
            PackedExt.NotPacked => GcCode.ArrayGet,
            PackedExt.Unsigned => GcCode.ArrayGetU,
            _ => throw new InvalidDataException($"Undefined packedtype: {Sx}")
        };

        public InstArrayGet(PackedExt sx)
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
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                "Instruction {0} was invalid. Referenced Type was not array:{1}",Op.GetMnemonic(),compositeType);
            
            var fieldType = arrayType.ElementType;
            var t = fieldType.UnpackType();
            
            context.Assert(fieldType.ValidExtension(Sx),
                "Instruction {0} was invalid. Bad packing extension:{1}",Op.GetMnemonic(), Sx);
            
            context.OpStack.PopI32();
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
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                $"Instruction {Op.GetMnemonic()} failed. Referenced Type was not array:{compositeType}");
            //6
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand at top of stack:{context.OpStack.Peek().Type}");
            //7
            var i = context.OpStack.PopI32();
            //8
            var refType = ValType.NullableRef | (ValType)X;
            context.Assert(context.OpStack.Peek().IsRef(refType),
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand at top of stack:{context.OpStack.Peek().Type}");
            //9
            var refVal = context.OpStack.PopType(refType);
            //10
            if (refVal.IsNullRef)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Ref was null");
            //11,12
            var a = refVal.GcRef as StoreArray;
            context.Assert(a,
                $"Instruction {Op.GetMnemonic()} failed. Ref was not an array.");
            //13
            context.Assert(context.Store.Contains(a.ArrayIndex),
                $"Instruction {Op.GetMnemonic()} failed. Store did not contain array reference {a.ArrayIndex.Value}");
            //14
            if (i >= a.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. {i} is beyond the array bounds {a.Length}");
            //15
            var fieldVal = a[i];
            //16
            if (Sx != PackedExt.NotPacked)
            {
                switch (arrayType.ElementType.StorageType)
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
            //17
            context.OpStack.PushValue(fieldVal);
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstArraySet : InstructionBase
    {
        private TypeIdx X;
        
        public override ByteCode Op => GcCode.StructSet;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the Context.",Op.GetMnemonic(), X);
            
            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                "Instruction {0} was invalid. Referenced Type was not array:{1}",Op.GetMnemonic(),compositeType);
            
            var fieldType = arrayType.ElementType;
            context.Assert(fieldType.Mut == Mutability.Mutable,
                "Instruction {0} was invalid. Cannot set immutable field:{1}",Op.GetMnemonic(), fieldType);
            
            var t = fieldType.UnpackType();
            var refType = ValType.NullableRef | (ValType)X;

            context.OpStack.PopType(t);
            context.OpStack.PopI32();
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
            //4
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                $"Instruction {Op.GetMnemonic()} was invalid. Referenced Type was not struct:{compositeType}");
            //5
            var ft = arrayType.ElementType;
            //6
            context.Assert( context.OpStack.Peek().IsType(ft.StorageType),
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7
            var val = context.OpStack.PopType(ft.StorageType);
            //8
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9
            var i = context.OpStack.PopI32();
            //10
            var refType = ValType.NullableRef | (ValType)X;
            context.Assert( context.OpStack.Peek().IsRef(refType),
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //11
            var refVal = context.OpStack.PopType(refType);
            //12
            if (refVal.IsNullRef)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Ref was null");
            //13,14
            var a = refVal.GcRef as StoreArray;
            context.Assert(a,
                $"Instruction {Op.GetMnemonic()} failed. Ref was not an array.");
            //15
            context.Assert(context.Store.Contains(a.ArrayIndex),
                $"Instruction {Op.GetMnemonic()} failed. Store did not contain array reference {a.ArrayIndex.Value}");
            //16
            if (i >= a.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Index {i} was beyond the array bounds {a.Length}");
            //17
            //18
            a[i] = val;
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }
    }

    public class InstArrayLen : InstructionBase
    {
        public override ByteCode Op => GcCode.ArrayLen;
        public override void Validate(IWasmValidationContext context)
        {
            var rt = context.OpStack.PopRefType();
            context.Assert(rt.Type.Matches(ValType.Array, context.Types),
                "Instruction {0} was invalid. Wrong operand type at top of stack:{1}", Op.GetMnemonic(), rt.Type);
            context.OpStack.PushI32();
        }

        public override void Execute(ExecContext context)
        {
            //1
            context.Assert(context.OpStack.Peek().IsRef(ValType.Array),
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type at top of stack: {context.OpStack.Peek().Type}");
            //2
            var refVal = context.OpStack.PopRefType();
            //3
            if (refVal.IsNullRef)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Ref was null");
            //4,5
            var a = refVal.GcRef as StoreArray;
            context.Assert(a,
                $"Instruction {Op.GetMnemonic()} failed. Ref was not an array.");
            //6
            context.Assert(context.Store.Contains(a.ArrayIndex),
                $"Instruction {Op.GetMnemonic()} failed. Store did not contain array reference {a.ArrayIndex.Value}");
            //7
            var n = a.Length;
            //8
            context.OpStack.PushI32(n);
        }
    }

    public class InstArrayFill : InstructionBase
    {
        private TypeIdx X;
        public override ByteCode Op => GcCode.ArrayFill;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the context.", Op.GetMnemonic(), X);
            
            var defType = context.Types[X];
            var compositeType = defType.Expansion;
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                "Instruction {0} was invalid. Referenced Type was not array:{1}",Op.GetMnemonic(),compositeType);
            
            var fieldType = arrayType.ElementType;
            context.Assert(fieldType.Mut == Mutability.Mutable,
                "Instruction {0} was invalid. Cannot set immutable field:{1}",Op.GetMnemonic(), fieldType);
            
            var t = fieldType.UnpackType();
            var refType = ValType.NullableRef | (ValType)X;

            context.OpStack.PopI32();
            context.OpStack.PopType(t);
            context.OpStack.PopI32();
            context.OpStack.PopType(refType);
            
        }

        public override void Execute(ExecContext context)
        {
            //1
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack");
            //2
            var n = context.OpStack.PopI32();
            //3
            context.Assert(context.OpStack.Count > 0,
                $"Instruction {Op.GetMnemonic()} failed. Operand stack underflow");
            //4
            var val = context.OpStack.PopAny();
            //5
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack");
            //6
            var d = context.OpStack.PopI32();
            //7
            var refType = ValType.NullableRef | (ValType)X;
            context.Assert(context.OpStack.Peek().IsRef(refType),
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack");
            //8
            var refVal = context.OpStack.PopRefType();
            //9
            if (refVal.IsNullRef)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Reference was null.");
            //10,11
            var a = refVal.GcRef as StoreArray;
            context.Assert(a,
                $"Instruction {Op.GetMnemonic()} failed. Reference was not an array.");
            //12
            context.Assert(context.Store.Contains(a.ArrayIndex),
                $"Instruction {Op.GetMnemonic()} failed. Store did not contain reference {a.ArrayIndex}");
            //13
            if (d + n > a.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Array overflow.");
            //14
            if (n == 0)
                return;
            //15 - 24 delegate the recursive part to StoreArray.Fill
            a.Fill(val, d, n);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstArrayCopy : InstructionBase
    {
        private TypeIdx X; //dest
        private TypeIdx Y; //src
        public override ByteCode Op => GcCode.ArrayFill;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction {0} was invalid. DefType {1} was not in the context.", Op.GetMnemonic(), X);
            
            var defTypeX = context.Types[X];
            var compositeTypeX = defTypeX.Expansion;
            var arrayTypeX = compositeTypeX as ArrayType;
            context.Assert(arrayTypeX,
                "Instruction {0} was invalid. Referenced Type was not array:{1}",Op.GetMnemonic(),compositeTypeX);
            
            var fieldTypeX = arrayTypeX.ElementType;
            context.Assert(fieldTypeX.Mut == Mutability.Mutable,
                "Instruction {0} was invalid. Cannot set immutable field:{1}",Op.GetMnemonic(), fieldTypeX);
            
            context.Assert(context.Types.Contains(Y),
                "Instruction {0} was invalid. DefType {1} was not in the context.", Op.GetMnemonic(), Y);
            
            var defTypeY = context.Types[Y];
            var compositeTypeY = defTypeY.Expansion;
            var arrayTypeY = compositeTypeY as ArrayType;
            context.Assert(arrayTypeY,
                "Instruction {0} was invalid. Referenced Type was not array:{1}",Op.GetMnemonic(),compositeTypeY);
            
            var fieldTypeY = arrayTypeY.ElementType;
            context.Assert(fieldTypeY.StorageType.Matches(fieldTypeX.StorageType, context.Types),
                "Instruction {0} was invalid. Array FieldType {1} does not match {2}",Op.GetMnemonic(), fieldTypeY, fieldTypeX);
            
            var refTypeX = ValType.NullableRef | (ValType)X;
            var refTypeY = ValType.NullableRef | (ValType)Y;

            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopType(refTypeY);
            context.OpStack.PopI32();
            context.OpStack.PopType(refTypeX);
        }

        public override void Execute(ExecContext context)
        {
            //2
            context.Assert(context.Frame.Module.Types.Contains(Y),
                $"Instruction {Op.GetMnemonic()} failed. DefType {Y} was not in the context");
            //3
            var defType = context.Frame.Module.Types[Y];
            var compositeType = defType.Expansion;
            //4
            var arrayType = compositeType as ArrayType;
            context.Assert(arrayType,
                $"Instruction {Op.GetMnemonic()} was invalid. Referenced Type was not struct:{compositeType}");
            //5
            var st = arrayType.ElementType;
            //6
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack");
            //7
            var n = context.OpStack.PopI32();
            //8
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack");
            //9
            var s = context.OpStack.PopI32();
            //10
            var refNullY = ValType.NullableRef | (ValType)Y;
            context.Assert(context.OpStack.Peek().IsRef(refNullY),
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack");
            //11
            var ref2 = context.OpStack.PopType(refNullY);
            //12
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack");
            //13
            var d = context.OpStack.PopI32();
            //14
            var refNullX = ValType.NullableRef | (ValType)X;
            context.Assert(context.OpStack.Peek().IsRef(refNullX),
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack");
            //15
            var ref1 = context.OpStack.PopType(refNullX);
            //16
            if (ref1.IsNullRef)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Array reference was null.");
            //17,18
            var a1 = ref1.GcRef as StoreArray;
            context.Assert(a1,
                $"Instruction {Op.GetMnemonic()} failed. Reference was not an array.");
            //19
            if (ref2.IsNullRef)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Array reference was null.");
            //20,21
            var a2 = ref2.GcRef as StoreArray;
            context.Assert(a2,
                $"Instruction {Op.GetMnemonic()} failed. Reference was not an array.");
            //22
            context.Assert(context.Store.Contains(a1.ArrayIndex),
                $"Instruction {Op.GetMnemonic()} failed. Store did not contain reference {a1.ArrayIndex}");
            //23
            context.Assert(context.Store.Contains(a2.ArrayIndex),
                $"Instruction {Op.GetMnemonic()} failed. Store did not contain reference {a2.ArrayIndex}");
            //24
            if (d+n > a1.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Destination array overflow.");
            //25
            if (s+n > a2.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Source array overflow.");
            //26
            if (n == 0)
                return;

            //27 - 30 delegate the recursive part to StoreArray.Copy
            a2.Copy(s, a1, d, n);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            Y = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
}