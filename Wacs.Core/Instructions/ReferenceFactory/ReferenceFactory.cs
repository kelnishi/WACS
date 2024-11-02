using System;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions
{
    public partial class ReferenceFactory : IInstructionFactory
    {
        public static readonly ReferenceFactory Factory = new();
        public ReferenceFactory() {}

        public T CreateInstruction<T>(ByteCode code)
            where T : InstructionBase =>
            CreateInstruction(code) as T ?? throw new InvalidOperationException($"Could not create instruction of type {typeof(T).Name} for the given ByteCode.");

        public IInstruction CreateInstruction(ByteCode opcode) => opcode.x00 switch {
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
            OpCode.Else              => InstElse.Inst,
            OpCode.End               => InstEnd.Inst,
                 
            OpCode.Br                => new InstBranch(),
            OpCode.BrIf              => new InstBranchConditional(),
            OpCode.BrTable           => new InstBranchTable(),
                 
            OpCode.Return            => InstReturn.Inst,
            OpCode.Call              => new InstCall(),
            OpCode.CallIndirect      => new InstCallIndirect(),
            //When invoking externally
            OpCode.Func              => new InstCall(),
                 
            // Reference Types 
            OpCode.RefNull           => new InstRefNull(),
            OpCode.RefIsNull         => InstRefIsNull.Inst,
            OpCode.RefFunc           => new InstRefFunc(),
                
            //Parametric Instructions
            OpCode.Drop              => InstDrop.Inst,
            OpCode.Select            => InstSelect.InstWithoutTypes,
            OpCode.SelectT           => new InstSelect(true),
                
            //Variable Instructions
            OpCode.LocalGet         => LocalVariableInst.CreateInstLocalGet(),
            OpCode.LocalSet         => LocalVariableInst.CreateInstLocalSet(),
            OpCode.LocalTee         => LocalVariableInst.CreateInstLocalTee(),
            OpCode.GlobalGet        => GlobalVariableInst.CreateInstGlobalGet(),
            OpCode.GlobalSet        => GlobalVariableInst.CreateInstGlobalSet(),
                
            //Table Instructions
            OpCode.TableGet          => new InstTableGet(),
            OpCode.TableSet          => new InstTableSet(),

            //Memory Instructions 
            OpCode.I32Load           => new InstMemoryLoad(ValType.I32, BitWidth.U32),
            OpCode.I64Load           => new InstMemoryLoad(ValType.I64, BitWidth.U64),
            OpCode.F32Load           => new InstMemoryLoad(ValType.F32, BitWidth.U32),
            OpCode.F64Load           => new InstMemoryLoad(ValType.F64, BitWidth.U64),
            OpCode.I32Load8S         => new InstMemoryLoad(ValType.I32, BitWidth.S8),
            OpCode.I32Load8U         => new InstMemoryLoad(ValType.I32, BitWidth.U8),
            OpCode.I32Load16S        => new InstMemoryLoad(ValType.I32, BitWidth.S16),
            OpCode.I32Load16U        => new InstMemoryLoad(ValType.I32, BitWidth.U16),
            OpCode.I64Load8S         => new InstMemoryLoad(ValType.I64, BitWidth.S8),
            OpCode.I64Load8U         => new InstMemoryLoad(ValType.I64, BitWidth.U8),
            OpCode.I64Load16S        => new InstMemoryLoad(ValType.I64, BitWidth.S16),
            OpCode.I64Load16U        => new InstMemoryLoad(ValType.I64, BitWidth.U16),
            OpCode.I64Load32S        => new InstMemoryLoad(ValType.I64, BitWidth.S32),
            OpCode.I64Load32U        => new InstMemoryLoad(ValType.I64, BitWidth.U32),
             
            OpCode.I32Store          => new InstMemoryStore(ValType.I32, BitWidth.U32),
            OpCode.I64Store          => new InstMemoryStore(ValType.I64, BitWidth.U64),
            OpCode.F32Store          => new InstMemoryStore(ValType.F32, BitWidth.U32),
            OpCode.F64Store          => new InstMemoryStore(ValType.F64, BitWidth.U64),
            OpCode.I32Store8         => new InstMemoryStore(ValType.I32, BitWidth.U8),
            OpCode.I32Store16        => new InstMemoryStore(ValType.I32, BitWidth.U16),
            OpCode.I64Store8         => new InstMemoryStore(ValType.I64, BitWidth.U8),
            OpCode.I64Store16        => new InstMemoryStore(ValType.I64, BitWidth.U16),
            OpCode.I64Store32        => new InstMemoryStore(ValType.I64, BitWidth.U32),
            
            OpCode.MemorySize        => new InstMemorySize(),
            OpCode.MemoryGrow        => new InstMemoryGrow(),
                 
            // Numeric Instructions 
            OpCode.I32Const          => new InstI32Const(),
            OpCode.I64Const          => new InstI64Const(),
            OpCode.F32Const          => new InstF32Const(),
            OpCode.F64Const          => new InstF64Const(),
            //I32 Comparison 
            OpCode.I32Eqz            => NumericInst.I32Eqz,
            OpCode.I32Eq             => NumericInst.I32Eq,
            OpCode.I32Ne             => NumericInst.I32Ne,
            OpCode.I32LtS            => NumericInst.I32LtS,
            OpCode.I32LtU            => NumericInst.I32LtU,
            OpCode.I32GtS            => NumericInst.I32GtS,
            OpCode.I32GtU            => NumericInst.I32GtU,
            OpCode.I32LeS            => NumericInst.I32LeS,
            OpCode.I32LeU            => NumericInst.I32LeU,
            OpCode.I32GeS            => NumericInst.I32GeS,
            OpCode.I32GeU            => NumericInst.I32GeU,
            //I64 Comparison 
            OpCode.I64Eqz            => NumericInst.I64Eqz,
            OpCode.I64Eq             => NumericInst.I64Eq,
            OpCode.I64Ne             => NumericInst.I64Ne,
            OpCode.I64LtS            => NumericInst.I64LtS,
            OpCode.I64LtU            => NumericInst.I64LtU,
            OpCode.I64GtS            => NumericInst.I64GtS,
            OpCode.I64GtU            => NumericInst.I64GtU,
            OpCode.I64LeS            => NumericInst.I64LeS,
            OpCode.I64LeU            => NumericInst.I64LeU,
            OpCode.I64GeS            => NumericInst.I64GeS,
            OpCode.I64GeU            => NumericInst.I64GeU,
            //F32 Comparison 
            OpCode.F32Eq             => NumericInst.F32Eq,
            OpCode.F32Ne             => NumericInst.F32Ne,
            OpCode.F32Lt             => NumericInst.F32Lt,
            OpCode.F32Gt             => NumericInst.F32Gt,
            OpCode.F32Le             => NumericInst.F32Le,
            OpCode.F32Ge             => NumericInst.F32Ge,
            //F64 Comparison 
            OpCode.F64Eq             => NumericInst.F64Eq,
            OpCode.F64Ne             => NumericInst.F64Ne,
            OpCode.F64Lt             => NumericInst.F64Lt,
            OpCode.F64Gt             => NumericInst.F64Gt,
            OpCode.F64Le             => NumericInst.F64Le,
            OpCode.F64Ge             => NumericInst.F64Ge,
            //I32 Math 
            OpCode.I32Clz            => NumericInst.I32Clz,   
            OpCode.I32Ctz            => NumericInst.I32Ctz,   
            OpCode.I32Popcnt         => NumericInst.I32Popcnt,
            OpCode.I32Add            => NumericInst.I32Add,   
            OpCode.I32Sub            => NumericInst.I32Sub,   
            OpCode.I32Mul            => NumericInst.I32Mul,   
            OpCode.I32DivS           => NumericInst.I32DivS,  
            OpCode.I32DivU           => NumericInst.I32DivU,  
            OpCode.I32RemS           => NumericInst.I32RemS,  
            OpCode.I32RemU           => NumericInst.I32RemU,  
            OpCode.I32And            => NumericInst.I32And,   
            OpCode.I32Or             => NumericInst.I32Or,    
            OpCode.I32Xor            => NumericInst.I32Xor,   
            OpCode.I32Shl            => NumericInst.I32Shl,   
            OpCode.I32ShrS           => NumericInst.I32ShrS,  
            OpCode.I32ShrU           => NumericInst.I32ShrU,  
            OpCode.I32Rotl           => NumericInst.I32Rotl,  
            OpCode.I32Rotr           => NumericInst.I32Rotr,  
            //I64 Math 
            OpCode.I64Clz            => NumericInst.I64Clz,   
            OpCode.I64Ctz            => NumericInst.I64Ctz,   
            OpCode.I64Popcnt         => NumericInst.I64Popcnt,
            OpCode.I64Add            => NumericInst.I64Add,   
            OpCode.I64Sub            => NumericInst.I64Sub,   
            OpCode.I64Mul            => NumericInst.I64Mul,   
            OpCode.I64DivS           => NumericInst.I64DivS,  
            OpCode.I64DivU           => NumericInst.I64DivU,  
            OpCode.I64RemS           => NumericInst.I64RemS,  
            OpCode.I64RemU           => NumericInst.I64RemU,  
            OpCode.I64And            => NumericInst.I64And,   
            OpCode.I64Or             => NumericInst.I64Or,    
            OpCode.I64Xor            => NumericInst.I64Xor,   
            OpCode.I64Shl            => NumericInst.I64Shl,   
            OpCode.I64ShrS           => NumericInst.I64ShrS,  
            OpCode.I64ShrU           => NumericInst.I64ShrU,  
            OpCode.I64Rotl           => NumericInst.I64Rotl,  
            OpCode.I64Rotr           => NumericInst.I64Rotr,  
            //F32 Math 
            OpCode.F32Abs            => NumericInst.F32Abs,
            OpCode.F32Neg            => NumericInst.F32Neg,
            OpCode.F32Ceil           => NumericInst.F32Ceil,
            OpCode.F32Floor          => NumericInst.F32Floor,
            OpCode.F32Trunc          => NumericInst.F32Trunc,
            OpCode.F32Nearest        => NumericInst.F32Nearest,
            OpCode.F32Sqrt           => NumericInst.F32Sqrt,
            OpCode.F32Add            => NumericInst.F32Add,
            OpCode.F32Sub            => NumericInst.F32Sub,
            OpCode.F32Mul            => NumericInst.F32Mul,
            OpCode.F32Div            => NumericInst.F32Div,
            OpCode.F32Min            => NumericInst.F32Min,
            OpCode.F32Max            => NumericInst.F32Max,
            OpCode.F32Copysign       => NumericInst.F32Copysign,
            //F64 Math
            OpCode.F64Abs            => NumericInst.F64Abs,
            OpCode.F64Neg            => NumericInst.F64Neg,
            OpCode.F64Ceil           => NumericInst.F64Ceil,
            OpCode.F64Floor          => NumericInst.F64Floor,
            OpCode.F64Trunc          => NumericInst.F64Trunc,
            OpCode.F64Nearest        => NumericInst.F64Nearest,
            OpCode.F64Sqrt           => NumericInst.F64Sqrt,
            OpCode.F64Add            => NumericInst.F64Add,
            OpCode.F64Sub            => NumericInst.F64Sub,
            OpCode.F64Mul            => NumericInst.F64Mul,
            OpCode.F64Div            => NumericInst.F64Div,
            OpCode.F64Min            => NumericInst.F64Min,
            OpCode.F64Max            => NumericInst.F64Max,
            OpCode.F64Copysign       => NumericInst.F64Copysign,
            //Conversions
            OpCode.I32WrapI64        => NumericInst.I32WrapI64,
            OpCode.I32TruncF32S      => NumericInst.I32TruncF32S,
            OpCode.I32TruncF32U      => NumericInst.I32TruncF32U,
            OpCode.I32TruncF64S      => NumericInst.I32TruncF64S,
            OpCode.I32TruncF64U      => NumericInst.I32TruncF64U,
            OpCode.I64ExtendI32S     => NumericInst.I64ExtendI32S,
            OpCode.I64ExtendI32U     => NumericInst.I64ExtendI32U,
            OpCode.I64TruncF32S      => NumericInst.I64TruncF32S,
            OpCode.I64TruncF32U      => NumericInst.I64TruncF32U,
            OpCode.I64TruncF64S      => NumericInst.I64TruncF64S,
            OpCode.I64TruncF64U      => NumericInst.I64TruncF64U,
            OpCode.F32ConvertI32S    => NumericInst.F32ConvertI32S,
            OpCode.F32ConvertI32U    => NumericInst.F32ConvertI32U,
            OpCode.F32ConvertI64S    => NumericInst.F32ConvertI64S,
            OpCode.F32ConvertI64U    => NumericInst.F32ConvertI64U,
            OpCode.F32DemoteF64      => NumericInst.F32DemoteF64,
            OpCode.F64ConvertI32S    => NumericInst.F64ConvertI32S,
            OpCode.F64ConvertI32U    => NumericInst.F64ConvertI32U,
            OpCode.F64ConvertI64S    => NumericInst.F64ConvertI64S,
            OpCode.F64ConvertI64U    => NumericInst.F64ConvertI64U,
            OpCode.F64PromoteF32     => NumericInst.F64PromoteF32,
            OpCode.I32ReinterpretF32 => NumericInst.I32ReinterpretF32,
            OpCode.I64ReinterpretF64 => NumericInst.I64ReinterpretF64,
            OpCode.F32ReinterpretI32 => NumericInst.F32ReinterpretI32,
            OpCode.F64ReinterpretI64 => NumericInst.F64ReinterpretI64,
            
            //Sign-Extension
            OpCode.I32Extend8S       => NumericInst.I32Extend8S,
            OpCode.I32Extend16S      => NumericInst.I32Extend16S,
            OpCode.I64Extend8S       => NumericInst.I64Extend8S,
            OpCode.I64Extend16S      => NumericInst.I64Extend16S,
            OpCode.I64Extend32S      => NumericInst.I64Extend32S,
            
            _ => throw new NotSupportedException($"Opcode {opcode} is not supported.")
        } ?? throw new InvalidOperationException($"Could not create instruction for opcode {opcode}");
    }
}