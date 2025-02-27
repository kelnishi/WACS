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
using Wacs.Core.Instructions.Memory;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.Instructions.Reference;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions
{
    public partial class SpecFactory : InstructionBaseFactory
    {
        public static readonly SpecFactory Factory = new();
        public SpecFactory() {}

        public T CreateInstruction<T>(ByteCode code)
            where T : InstructionBase =>
            CreateInstruction(code) as T ?? throw new InvalidOperationException($"Could not create instruction of type {typeof(T).Name} for the given ByteCode.");

        public InstructionBase CreateInstruction(ByteCode opcode) => opcode.x00 switch {
            OpCode.FB                => CreateInstruction(opcode.xFB),
            OpCode.FC                => CreateInstruction(opcode.xFC),
            OpCode.FD                => CreateInstruction(opcode.xFD),
            OpCode.FE                => CreateInstruction(opcode.xFE),
            
            //Control Instructions
            OpCode.Unreachable       => InstUnreachable.Inst,
            OpCode.Nop               => InstNop.Inst,
            OpCode.Block             => new InstBlock(),
            OpCode.Loop              => new InstLoop(),
            OpCode.If                => new InstIf(),
            OpCode.Else              => new InstElse(),
            OpCode.End               => new InstEnd(),
            
            OpCode.TryTable          => new InstTryTable(),
            OpCode.Throw             => new InstThrow(),
            OpCode.ThrowRef          => new InstThrowRef(),
                 
            OpCode.Br                => new InstBranch(),
            OpCode.BrIf              => new InstBranchIf(),
            OpCode.BrTable           => new InstBranchTable(),
                 
            OpCode.Return            => InstReturn.Inst,
            OpCode.Call              => new InstCall(),
            OpCode.CallIndirect      => new InstCallIndirect(),
            OpCode.CallRef           => new InstCallRef(),
            
            OpCode.ReturnCall         => new InstReturnCall(),
            OpCode.ReturnCallIndirect => new InstReturnCallIndirect(),
            OpCode.ReturnCallRef      => new InstReturnCallRef(),
                 
            // Reference Types 
            OpCode.RefNull           => new InstRefNull(),
            OpCode.RefIsNull         => InstRefIsNull.Inst,
            OpCode.RefFunc           => new InstRefFunc(),
            
            OpCode.RefEq             => InstRefEq.Inst,
            OpCode.RefAsNonNull      => InstRefAsNonNull.Inst,
            
            OpCode.BrOnNull          => new InstBrOnNull(),
            OpCode.BrOnNonNull       => new InstBrOnNonNull(),
                
            //Parametric Instructions
            OpCode.Drop              => InstDrop.Inst,
            OpCode.Select            => InstSelect.InstWithoutTypes,
            OpCode.SelectT           => new InstSelect(true),
                
            //Variable Instructions
            OpCode.LocalGet         => new InstLocalGet(),
            OpCode.LocalSet         => new InstLocalSet(),
            OpCode.LocalTee         => new InstLocalTee(),
            OpCode.GlobalGet        => new InstGlobalGet(),
            OpCode.GlobalSet        => new InstGlobalSet(),
                
            //Table Instructions
            OpCode.TableGet          => new InstTableGet(),
            OpCode.TableSet          => new InstTableSet(),

            //Memory Instructions 
            OpCode.I32Load           => new InstI32Load(),
            OpCode.I64Load           => new InstI64Load(),
            OpCode.F32Load           => new InstF32Load(),
            OpCode.F64Load           => new InstF64Load(),
            OpCode.I32Load8S         => new InstI32Load8S(),
            OpCode.I32Load8U         => new InstI32Load8U(),
            OpCode.I32Load16S        => new InstI32Load16S(),
            OpCode.I32Load16U        => new InstI32Load16U(),
            OpCode.I64Load8S         => new InstI64Load8S(),
            OpCode.I64Load8U         => new InstI64Load8U(),
            OpCode.I64Load16S        => new InstI64Load16S(),
            OpCode.I64Load16U        => new InstI64Load16U(),
            OpCode.I64Load32S        => new InstI64Load32S(),
            OpCode.I64Load32U        => new InstI64Load32U(),
             
            OpCode.I32Store          => new InstI32Store(),
            OpCode.I64Store          => new InstI64Store(),
            OpCode.F32Store          => new InstF32Store(),
            OpCode.F64Store          => new InstF64Store(),
            OpCode.I32Store8         => new InstI32Store8(),
            OpCode.I32Store16        => new InstI32Store16(),
            OpCode.I64Store8         => new InstI64Store8(),
            OpCode.I64Store16        => new InstI64Store16(),
            OpCode.I64Store32        => new InstI64Store32(),
            
            OpCode.MemorySize        => new InstMemorySize(),
            OpCode.MemoryGrow        => new InstMemoryGrow(),
                 
            // Numeric Instructions 
            OpCode.I32Const          => new InstI32Const(),
            OpCode.I64Const          => new InstI64Const(),
            OpCode.F32Const          => new InstF32Const(),
            OpCode.F64Const          => new InstF64Const(),
            //I32 Comparison 
            OpCode.I32Eqz            => InstI32TestOp.I32Eqz,
            OpCode.I32Eq             => InstI32RelOp.I32Eq,
            OpCode.I32Ne             => InstI32RelOp.I32Ne,
            OpCode.I32LtS            => InstI32RelOp.I32LtS,
            OpCode.I32LtU            => InstI32RelOp.I32LtU,
            OpCode.I32GtS            => InstI32RelOp.I32GtS,
            OpCode.I32GtU            => InstI32RelOp.I32GtU,
            OpCode.I32LeS            => InstI32RelOp.I32LeS,
            OpCode.I32LeU            => InstI32RelOp.I32LeU,
            OpCode.I32GeS            => InstI32RelOp.I32GeS,
            OpCode.I32GeU            => InstI32RelOp.I32GeU,
            //I64 Comparison 
            OpCode.I64Eqz            => InstI64TestOp.I64Eqz,
            OpCode.I64Eq             => InstI64RelOp.I64Eq,
            OpCode.I64Ne             => InstI64RelOp.I64Ne,
            OpCode.I64LtS            => InstI64RelOp.I64LtS,
            OpCode.I64LtU            => InstI64RelOp.I64LtU,
            OpCode.I64GtS            => InstI64RelOp.I64GtS,
            OpCode.I64GtU            => InstI64RelOp.I64GtU,
            OpCode.I64LeS            => InstI64RelOp.I64LeS,
            OpCode.I64LeU            => InstI64RelOp.I64LeU,
            OpCode.I64GeS            => InstI64RelOp.I64GeS,
            OpCode.I64GeU            => InstI64RelOp.I64GeU,
            //F32 Comparison 
            OpCode.F32Eq             => InstF32RelOp.F32Eq,
            OpCode.F32Ne             => InstF32RelOp.F32Ne,
            OpCode.F32Lt             => InstF32RelOp.F32Lt,
            OpCode.F32Gt             => InstF32RelOp.F32Gt,
            OpCode.F32Le             => InstF32RelOp.F32Le,
            OpCode.F32Ge             => InstF32RelOp.F32Ge,
            //F64 Comparison 
            OpCode.F64Eq             => InstF64RelOp.F64Eq,
            OpCode.F64Ne             => InstF64RelOp.F64Ne,
            OpCode.F64Lt             => InstF64RelOp.F64Lt,
            OpCode.F64Gt             => InstF64RelOp.F64Gt,
            OpCode.F64Le             => InstF64RelOp.F64Le,
            OpCode.F64Ge             => InstF64RelOp.F64Ge,
            //I32 Math 
            OpCode.I32Clz            => InstI32UnOp.I32Clz,   
            OpCode.I32Ctz            => InstI32UnOp.I32Ctz,   
            OpCode.I32Popcnt         => InstI32UnOp.I32Popcnt,
            OpCode.I32Add            => InstI32BinOp.I32Add,   
            OpCode.I32Sub            => InstI32BinOp.I32Sub,   
            OpCode.I32Mul            => InstI32BinOp.I32Mul,   
            OpCode.I32DivS           => InstI32BinOp.I32DivS,  
            OpCode.I32DivU           => InstI32BinOp.I32DivU,  
            OpCode.I32RemS           => InstI32BinOp.I32RemS,  
            OpCode.I32RemU           => InstI32BinOp.I32RemU,  
            OpCode.I32And            => InstI32BinOp.I32And,   
            OpCode.I32Or             => InstI32BinOp.I32Or,    
            OpCode.I32Xor            => InstI32BinOp.I32Xor,   
            OpCode.I32Shl            => InstI32BinOp.I32Shl,   
            OpCode.I32ShrS           => InstI32BinOp.I32ShrS,  
            OpCode.I32ShrU           => InstI32BinOp.I32ShrU,  
            OpCode.I32Rotl           => InstI32BinOp.I32Rotl,  
            OpCode.I32Rotr           => InstI32BinOp.I32Rotr,  
            //I64 Math 
            OpCode.I64Clz            => InstI64UnOp.I64Clz,   
            OpCode.I64Ctz            => InstI64UnOp.I64Ctz,   
            OpCode.I64Popcnt         => InstI64UnOp.I64Popcnt,
            OpCode.I64Add            => InstI64BinOp.I64Add,   
            OpCode.I64Sub            => InstI64BinOp.I64Sub,   
            OpCode.I64Mul            => InstI64BinOp.I64Mul,   
            OpCode.I64DivS           => InstI64BinOp.I64DivS,  
            OpCode.I64DivU           => InstI64BinOp.I64DivU,  
            OpCode.I64RemS           => InstI64BinOp.I64RemS,  
            OpCode.I64RemU           => InstI64BinOp.I64RemU,  
            OpCode.I64And            => InstI64BinOp.I64And,   
            OpCode.I64Or             => InstI64BinOp.I64Or,    
            OpCode.I64Xor            => InstI64BinOp.I64Xor,   
            OpCode.I64Shl            => InstI64BinOp.I64Shl,   
            OpCode.I64ShrS           => InstI64BinOp.I64ShrS,  
            OpCode.I64ShrU           => InstI64BinOp.I64ShrU,  
            OpCode.I64Rotl           => InstI64BinOp.I64Rotl,  
            OpCode.I64Rotr           => InstI64BinOp.I64Rotr,  
            //F32 Math 
            OpCode.F32Abs            => InstF32UnOp.F32Abs,
            OpCode.F32Neg            => InstF32UnOp.F32Neg,
            OpCode.F32Ceil           => InstF32UnOp.F32Ceil,
            OpCode.F32Floor          => InstF32UnOp.F32Floor,
            OpCode.F32Trunc          => InstF32UnOp.F32Trunc,
            OpCode.F32Nearest        => InstF32UnOp.F32Nearest,
            OpCode.F32Sqrt           => InstF32UnOp.F32Sqrt,
            OpCode.F32Add            => InstF32BinOp.F32Add,
            OpCode.F32Sub            => InstF32BinOp.F32Sub,
            OpCode.F32Mul            => InstF32BinOp.F32Mul,
            OpCode.F32Div            => InstF32BinOp.F32Div,
            OpCode.F32Min            => InstF32BinOp.F32Min,
            OpCode.F32Max            => InstF32BinOp.F32Max,
            OpCode.F32Copysign       => InstF32BinOp.F32Copysign,
            //F64 Math
            OpCode.F64Abs            => InstF64UnOp.F64Abs,
            OpCode.F64Neg            => InstF64UnOp.F64Neg,
            OpCode.F64Ceil           => InstF64UnOp.F64Ceil,
            OpCode.F64Floor          => InstF64UnOp.F64Floor,
            OpCode.F64Trunc          => InstF64UnOp.F64Trunc,
            OpCode.F64Nearest        => InstF64UnOp.F64Nearest,
            OpCode.F64Sqrt           => InstF64UnOp.F64Sqrt,
            OpCode.F64Add            => InstF64BinOp.F64Add,
            OpCode.F64Sub            => InstF64BinOp.F64Sub,
            OpCode.F64Mul            => InstF64BinOp.F64Mul,
            OpCode.F64Div            => InstF64BinOp.F64Div,
            OpCode.F64Min            => InstF64BinOp.F64Min,
            OpCode.F64Max            => InstF64BinOp.F64Max,
            OpCode.F64Copysign       => InstF64BinOp.F64Copysign,
            //Conversions
            OpCode.I32WrapI64        => InstConvert.I32WrapI64,
            OpCode.I32TruncF32S      => InstConvert.I32TruncF32S,
            OpCode.I32TruncF32U      => InstConvert.I32TruncF32U,
            OpCode.I32TruncF64S      => InstConvert.I32TruncF64S,
            OpCode.I32TruncF64U      => InstConvert.I32TruncF64U,
            OpCode.I64ExtendI32S     => InstConvert.I64ExtendI32S,
            OpCode.I64ExtendI32U     => InstConvert.I64ExtendI32U,
            OpCode.I64TruncF32S      => InstConvert.I64TruncF32S,
            OpCode.I64TruncF32U      => InstConvert.I64TruncF32U,
            OpCode.I64TruncF64S      => InstConvert.I64TruncF64S,
            OpCode.I64TruncF64U      => InstConvert.I64TruncF64U,
            OpCode.F32ConvertI32S    => InstConvert.F32ConvertI32S,
            OpCode.F32ConvertI32U    => InstConvert.F32ConvertI32U,
            OpCode.F32ConvertI64S    => InstConvert.F32ConvertI64S,
            OpCode.F32ConvertI64U    => InstConvert.F32ConvertI64U,
            OpCode.F32DemoteF64      => InstConvert.F32DemoteF64,
            OpCode.F64ConvertI32S    => InstConvert.F64ConvertI32S,
            OpCode.F64ConvertI32U    => InstConvert.F64ConvertI32U,
            OpCode.F64ConvertI64S    => InstConvert.F64ConvertI64S,
            OpCode.F64ConvertI64U    => InstConvert.F64ConvertI64U,
            OpCode.F64PromoteF32     => InstConvert.F64PromoteF32,
            OpCode.I32ReinterpretF32 => InstConvert.I32ReinterpretF32,
            OpCode.I64ReinterpretF64 => InstConvert.I64ReinterpretF64,
            OpCode.F32ReinterpretI32 => InstConvert.F32ReinterpretI32,
            OpCode.F64ReinterpretI64 => InstConvert.F64ReinterpretI64,
            
            //Sign-Extension
            OpCode.I32Extend8S       => InstI32SignExtend.I32Extend8S,
            OpCode.I32Extend16S      => InstI32SignExtend.I32Extend16S,
            OpCode.I64Extend8S       => InstI64SignExtend.I64Extend8S,
            OpCode.I64Extend16S      => InstI64SignExtend.I64Extend16S,
            OpCode.I64Extend32S      => InstI64SignExtend.I64Extend32S,
            
            _ => throw new NotSupportedException($"Opcode {opcode} is not supported.")
        } ?? throw new InvalidOperationException($"Could not create instruction for opcode {opcode}");
    }
}