// Copyright 2025 Kelvin Nishikawa
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

namespace Wacs.Core.OpCodes
{
    public partial struct ByteCode
    {
        public static readonly ByteCode ArrayNew = GcCode.ArrayNew;
        public static readonly ByteCode ArrayNewDefault = GcCode.ArrayNewDefault;
        public static readonly ByteCode ArrayNewFixed = GcCode.ArrayNewFixed;
        public static readonly ByteCode ArrayNewData = GcCode.ArrayNewData;
        public static readonly ByteCode ArrayNewElem = GcCode.ArrayNewElem;
        public static readonly ByteCode ArrayGetS = GcCode.ArrayGetS;
        public static readonly ByteCode ArrayGet = GcCode.ArrayGet;
        public static readonly ByteCode ArrayGetU = GcCode.ArrayGetU;
        public static readonly ByteCode ArraySet = GcCode.ArraySet;
        public static readonly ByteCode ArrayLen = GcCode.ArrayLen;
        public static readonly ByteCode ArrayFill = GcCode.ArrayFill;
        public static readonly ByteCode ArrayCopy = GcCode.ArrayCopy;
        public static readonly ByteCode ArrayInitData = GcCode.ArrayInitData;
        public static readonly ByteCode ArrayInitElem = GcCode.ArrayInitElem;
        public static readonly ByteCode BrOnCast = GcCode.BrOnCast;
        public static readonly ByteCode BrOnCastFail = GcCode.BrOnCastFail;
        public static readonly ByteCode AnyConvertExtern = GcCode.AnyConvertExtern;
        public static readonly ByteCode ExternConvertAny = GcCode.ExternConvertAny;
        public static readonly ByteCode RefI31 = GcCode.RefI31;
        public static readonly ByteCode I31GetS = GcCode.I31GetS;
        public static readonly ByteCode I31GetU = GcCode.I31GetU;
        public static readonly ByteCode StructNew = GcCode.StructNew;
        public static readonly ByteCode StructNewDefault = GcCode.StructNewDefault;
        public static readonly ByteCode StructSet = GcCode.StructSet;
        public static readonly ByteCode I32Const = OpCode.I32Const;
        public static readonly ByteCode I64Const = OpCode.I64Const;
        public static readonly ByteCode F32Const = OpCode.F32Const;
        public static readonly ByteCode F64Const = OpCode.F64Const;
        public static readonly ByteCode BrOnNull = OpCode.BrOnNull;
        public static readonly ByteCode BrOnNonNull = OpCode.BrOnNonNull;
        public static readonly ByteCode RefNull = OpCode.RefNull;
        public static readonly ByteCode RefIsNull = OpCode.RefIsNull;
        public static readonly ByteCode RefFunc = OpCode.RefFunc;
        public static readonly ByteCode RefEq = OpCode.RefEq;
        public static readonly ByteCode RefAsNonNull = OpCode.RefAsNonNull;
        public static readonly ByteCode RefCastNull = GcCode.RefCastNull;
        public static readonly ByteCode RefCast = GcCode.RefCast;
        public static readonly ByteCode RefTestNull = GcCode.RefTestNull;
        public static readonly ByteCode RefTest = GcCode.RefTest;
        public static readonly ByteCode V128Const = SimdCode.V128Const;
        public static readonly ByteCode I8x16Shuffle = SimdCode.I8x16Shuffle;
        public static readonly ByteCode Aggr1_0 = WacsCode.Aggr1_0;
        public static readonly ByteCode Aggr1_1 = WacsCode.Aggr1_1;
        public static readonly ByteCode Aggr2_0 = WacsCode.Aggr2_0;
        public static readonly ByteCode Aggr2_1 = WacsCode.Aggr2_1;
        public static readonly ByteCode Aggr3_1 = WacsCode.Aggr3_1;
        public static readonly ByteCode LocalGetSet = WacsCode.LocalGetSet;
        public static readonly ByteCode LocalConstSet = WacsCode.LocalConstSet;
        public static readonly ByteCode I32FusedAdd = WacsCode.I32FusedAdd;
        public static readonly ByteCode I32FusedSub = WacsCode.I32FusedSub;
        public static readonly ByteCode I32FusedMul = WacsCode.I32FusedMul;
        public static readonly ByteCode I32FusedAnd = WacsCode.I32FusedAnd;
        public static readonly ByteCode I32FusedOr = WacsCode.I32FusedOr;
        public static readonly ByteCode I64FusedAdd = WacsCode.I64FusedAdd;
        public static readonly ByteCode I64FusedSub = WacsCode.I64FusedSub;
        public static readonly ByteCode I64FusedMul = WacsCode.I64FusedMul;
        public static readonly ByteCode I64FusedAnd = WacsCode.I64FusedAnd;
        public static readonly ByteCode I64FusedOr = WacsCode.I64FusedOr;
        public static readonly ByteCode StackVal = WacsCode.StackVal;
        public static readonly ByteCode StackU32 = WacsCode.StackU32;
        public static readonly ByteCode StackI32 = WacsCode.StackI32;
        public static readonly ByteCode Unreachable = OpCode.Unreachable;
        public static readonly ByteCode Nop = OpCode.Nop;
        public static readonly ByteCode Block = OpCode.Block;
        public static readonly ByteCode Loop = OpCode.Loop;
        public static readonly ByteCode If = OpCode.If;
        public static readonly ByteCode Else = OpCode.Else;
        public static readonly ByteCode End = OpCode.End;
        public static readonly ByteCode Br = OpCode.Br;
        public static readonly ByteCode BrIf = OpCode.BrIf;
        public static readonly ByteCode BrTable = OpCode.BrTable;
        public static readonly ByteCode Return = OpCode.Return;
        public static readonly ByteCode Call = OpCode.Call;
        public static readonly ByteCode CallRef = OpCode.CallRef;
        public static readonly ByteCode CallIndirect = OpCode.CallIndirect;
        public static readonly ByteCode TryTable = OpCode.TryTable;
        public static readonly ByteCode Catch = WacsCode.Catch;
        public static readonly ByteCode Throw = OpCode.Throw;
        public static readonly ByteCode ThrowRef = OpCode.ThrowRef;
        public static readonly ByteCode GlobalGet = OpCode.GlobalGet;
        public static readonly ByteCode GlobalSet = OpCode.GlobalSet;
        public static readonly ByteCode LocalGet = OpCode.LocalGet;
        public static readonly ByteCode LocalSet = OpCode.LocalSet;
        public static readonly ByteCode LocalTee = OpCode.LocalTee;
        public static readonly ByteCode MemorySize = OpCode.MemorySize;
        public static readonly ByteCode MemoryGrow = OpCode.MemoryGrow;
        public static readonly ByteCode MemoryInit = ExtCode.MemoryInit;
        public static readonly ByteCode DataDrop = ExtCode.DataDrop;
        public static readonly ByteCode MemoryCopy = ExtCode.MemoryCopy;
        public static readonly ByteCode MemoryFill = ExtCode.MemoryFill;
        public static readonly ByteCode Drop = OpCode.Drop;
        public static readonly ByteCode Select = OpCode.Select;
        public static readonly ByteCode Expr = OpCode.Expr;
        public static readonly ByteCode Func = OpCode.Func;
        public static readonly ByteCode TableGet = OpCode.TableGet;
        public static readonly ByteCode TableSet = OpCode.TableSet;
        public static readonly ByteCode TableInit = ExtCode.TableInit;
        public static readonly ByteCode ElemDrop = ExtCode.ElemDrop;
        public static readonly ByteCode TableCopy = ExtCode.TableCopy;
        public static readonly ByteCode TableGrow = ExtCode.TableGrow;
        public static readonly ByteCode TableSize = ExtCode.TableSize;
        public static readonly ByteCode TableFill = ExtCode.TableFill;
        public static readonly ByteCode ReturnCall = OpCode.ReturnCall;
        public static readonly ByteCode ReturnCallIndirect = OpCode.ReturnCallIndirect;
    }
}