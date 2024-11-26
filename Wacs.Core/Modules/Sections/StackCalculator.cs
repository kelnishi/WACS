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
using Wacs.Core.Validation;

namespace Wacs.Core
{
    public class CalculatorOpStack : IValidationOpStack
    {
        private readonly StackCalculator _context;
        public CalculatorOpStack(StackCalculator ctx) => _context = ctx;

        public int Height => _context.stackHeight;

        public void Clear()
        {
            _context.Clear();
        }

        public void PushResult(ResultType types)
        {
            foreach (var type in types.Types) {
                _context.Push(type);
            }
        }

        public void PushI32(int i32 = 0) => _context.Push(ValType.I32);
        public void PushI64(long i64 = 0) => _context.Push(ValType.I64);
        public void PushF32(float f32 = 0) => _context.Push(ValType.F32);
        public void PushF64(double f64 = 0) => _context.Push(ValType.F64);
        public void PushV128(V128 v128 = default) => _context.Push(ValType.V128);
        public void PushFuncref(Value value) => _context.Push(ValType.Funcref);
        public void PushExternref(Value value) => _context.Push(ValType.Externref);
        public void PushType(ValType type) => _context.Push(type);

        public void PushValues(Stack<Value> vals) {
            while (vals.Count > 0) _context.Push(vals.Pop().Type);
        }

        public Value PopI32() => _context.Pop(ValType.I32);
        public Value PopI64() => _context.Pop(ValType.I64);
        public Value PopF32() => _context.Pop(ValType.F32);
        public Value PopF64() => _context.Pop(ValType.F64);
        public Value PopV128() => _context.Pop(ValType.V128);
        public Value PopRefType() => _context.Pop(ValType.Funcref);

        public Value PopType(ValType type) => _context.Pop(type);
        public Value PopAny() => _context.Pop(ValType.Nil);

        public Stack<Value> PopValues(ResultType types)
        {
            var aside = new Stack<Value>();
            foreach (var type in types.Types.Reverse())
            {
                var stackType = PopAny();
                aside.Push(stackType);
            }
            return aside;
        }

        public void ReturnResults(ResultType types)
        {
            foreach (var type in types.Types)
            {
                PushType(type);
            }
        }

        public void PopValues(ResultType types, bool keep = true)
        {
            var aside = new Stack<Value>();
            //Pop vals off the stack
            for (int i = 0, l = types.Types.Length; i < l; ++i)
            {
                var v = PopAny();
                aside.Push(v);
            }

            //Check that they match ResultType and push them back on
            foreach (var type in types.Types)
            {
                var p = aside.Pop();
                // if (p.Type != type)
                //     throw new ValidationException("Invalid Operand Stack did not match ResultType");
                
                if (keep)
                    PushType(p.Type);
            }
        }
    }
    
    
    public class StackCalculator: IWasmValidationContext
    {
        public StackCalculator(ModuleInstance moduleInst, Module.Function func)
        {
            Types = new TypesSpace(moduleInst.Repr);

            Funcs = new FunctionsSpace(moduleInst.Repr);
            Tables = new TablesSpace(moduleInst.Repr);
            Mems = new MemSpace(moduleInst.Repr);

            Elements = new ElementsSpace(moduleInst.Repr.Elements.ToList());
            Datas = new DataValidationSpace(moduleInst.Repr.Datas.Length);

            Globals = new GlobalValidationSpace(moduleInst.Repr);

            OpStack = new CalculatorOpStack(this);
            
            var funcType = Types[func.TypeIndex];
            var fakeType = new FunctionType(ResultType.Empty, funcType.ResultType);

            int capacity = funcType.ParameterTypes.Types.Length + func.Locals.Length;
            var localData = new Value[capacity];
            Locals = new LocalsSpace(localData, funcType.ParameterTypes.Types, func.Locals);
            
            ReturnType = funcType.ResultType;
            PushControlFrame(OpCode.Block, fakeType);
            Attributes = new RuntimeAttributes
            {
                Configure_RefTypes = true,
            };
        }

        internal int stackHeight = 0;
        public void Clear()
        {
            stackHeight = 0;
        }
        public void Push(ValType val)
        {
            stackHeight += 1;
        }

        public Value Pop(ValType type)
        {
            stackHeight -= 1;
            return new Value(type);
        }
        
        public RuntimeAttributes Attributes { get; }
        public IValidationOpStack OpStack { get; }
        public FuncIdx FunctionIndex { get; }
        public ResultType ReturnType { get; }
        public bool Unreachable { get; set; }
        public TypesSpace Types { get; }
        public FunctionsSpace Funcs { get; }
        public TablesSpace Tables { get; }
        public MemSpace Mems { get; }
        public GlobalValidationSpace Globals { get; }
        public LocalsSpace Locals { get; }
        public ElementsSpace Elements { get; set; }
        public DataValidationSpace Datas { get; set; }
        public bool ContainsLabel(uint label)
        {
            return true;
        }

        public Stack<ValidationControlFrame> ControlStack { get; } = new();
        public ValidationControlFrame ControlFrame => ControlStack.Peek();

        public void PushControlFrame(ByteCode opCode, FunctionType types)
        {
            var frame = new ValidationControlFrame
            {
                Opcode = opCode,
                Types = types,
                Height = OpStack.Height,
            };
            
            ControlStack.Push(frame);
            
            OpStack.PushResult(types.ParameterTypes);
        }

        public ValidationControlFrame PopControlFrame()
        {
            if (ControlStack.Count == 0)
                throw new InvalidDataException("Control Stack underflow");
            
            //Check to make sure we have the correct results
            OpStack.PopValues(ControlFrame.EndTypes);

            //Reset the stack
            stackHeight = ControlFrame.Height;
            
            return ControlStack.Pop();
        }

        public void SetUnreachable()
        {
            ControlFrame.Unreachable = true;
        }


        public void Assert(bool factIsTrue, string message) {}

        public void Assert(object objIsNotNull, string message) { }

        public void ValidateBlock(Block instructionBlock, int index = 0) {}
    }
}