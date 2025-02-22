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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    public class FakeOpStack : IValidationOpStack
    {
        private readonly FakeContext _context;
        public FakeOpStack(FakeContext ctx) => _context = ctx;

        public int Height => 0;

        public void Clear()
        {
            //We shouldn't be dealing with stack control
            throw new InvalidOperationException();
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
        public void PushRef(Value value) => _context.Push(ValType.None);
        public void PushFuncref(Value value) => _context.Push(ValType.FuncRef);
        public void PushExternref(Value value) => _context.Push(ValType.ExternRef);
        public void PushType(ValType type) => _context.Push(type);

        public void PushValues(Stack<Value> vals) {
            while (vals.Count > 0) _context.Push(vals.Pop().Type);
        }

        public void DiscardValues(ResultType types) {
            foreach (var type in types.Types.Reverse()) PopType(type);
        }

        public Value PopI32() => _context.Pop(ValType.I32);
        public Value PopI64() => _context.Pop(ValType.I64);
        public Value PopInt() => _context.Pop(ValType.I64);
        public Value PopF32() => _context.Pop(ValType.F32);
        public Value PopF64() => _context.Pop(ValType.F64);
        public Value PopV128() => _context.Pop(ValType.V128);
        public Value PopRefType() => _context.Pop(ValType.FuncRef);

        public Value PopType(ValType type) => _context.Pop(type);
        public Value PopAny() => _context.Pop(ValType.Nil);

        public void PopValues(ResultType types, ref Stack<Value> aside)
        {
            foreach (var type in types.Types.Reverse())
            {
                var stackType = PopAny();
                aside.Push(stackType);
            }
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
    
    public class FakeContext : IWasmValidationContext
    {
        private static readonly object NonNull = new();

        private static Stack<Value> _aside = new();
        private readonly FakeOpStack _opStack;

        private readonly Stack<string> fakeStack = new();
        private Module _module;

        public string LastEvent = "";

        public FakeContext(Module module, Module.Function func)
        {
            ModuleInst = new ModuleInstance(module);
            _module = module;

            Funcs = new FunctionsSpace(module);
            Tables = new TablesSpace(module);
            Mems = new MemSpace(module);

            Elements = new ElementsSpace(module.Elements.ToList());
            Datas = new DataValidationSpace(module.Datas.Length);

            Globals = new GlobalValidationSpace(module);
            Globals.IncrementalHighWatermark = int.MaxValue;
            
            Tags = new TagsSpace(module);

            _opStack = new FakeOpStack(this);
            
            var funcType = Types[func.TypeIndex].Expansion as FunctionType;
            var fakeType = new FunctionType(ResultType.Empty, funcType.ResultType);

            DummyContext = BuildDummyContext(module, ModuleInst, func);

            ReturnType = funcType.ResultType;
            PushControlFrame(OpCode.Block, fakeType);
            Attributes = new RuntimeAttributes();
        }

        private ModuleInstance ModuleInst { get; }

        public IValidationOpStack ReturnStack => _opStack;

        internal ExecContext DummyContext { get; set; }
        public RuntimeAttributes Attributes { get; set; }

        public IValidationOpStack OpStack => _opStack;


        public void Assert(bool factIsTrue, string formatString, params object[] args) { }
        public void Assert([NotNull] object? objIsNotNull, string formatString, params object[] args) { objIsNotNull = NonNull; }
        
        public FuncIdx FunctionIndex => FuncIdx.Default;

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
            OpStack.PopValues(ControlFrame.EndTypes, ref _aside);
            _aside.Clear();
            
            //Reset the stack
            if (OpStack.Height != ControlFrame.Height)
                throw new InvalidDataException($"Operand stack height {OpStack.Height} differed from Control Frame height {ControlFrame.Height}");
            
            return ControlStack.Pop();
        }


        public bool ContainsLabel(uint label) => ControlStack.Count - 2 >= label;

        public ResultType ReturnType { get; }

        public void ValidateBlock(Block instructionBlock, int index = 0) { }
        public void ValidateCatches(CatchType[] catches) { }

        public bool Unreachable { get; set; }

        public void SetUnreachable()
        {
            //reset the height to the controlstack height
            // Unreachable = true;
            ControlFrame.Unreachable = true;
        }

        public TypesSpace Types => ModuleInst.Types;
        public FunctionsSpace Funcs { get; }
        public TablesSpace Tables { get; }
        public MemSpace Mems { get; }
        public GlobalValidationSpace Globals { get; }
        public LocalsSpace Locals => DummyContext.Frame.Locals;
        public ElementsSpace Elements { get; set; }
        public DataValidationSpace Datas { get; set; }

        public TagsSpace Tags { get; }
        
        private ExecContext BuildDummyContext(Module module, ModuleInstance moduleInst, Module.Function modFunc)
        {
            var store = new Store();
            store.OpenTransaction();
            FakeHostDelegate fakeHostFunc = () => { };
            foreach (var import in module.Imports)
            {
                var entityId = (module: import.ModuleName, entity: import.Name);
                switch (import.Desc)
                {
                    case Module.ImportDesc.FuncDesc funcDesc:
                        var type = moduleInst.Types[funcDesc.TypeIndex].Expansion;
                        var funcSig = type as FunctionType;
                        if (funcSig is null)
                            throw new InvalidDataException($"Function had invalid type:{type}");
                        var funcAddr = store.AllocateHostFunction(entityId, funcSig, typeof(FakeHostDelegate), fakeHostFunc, false);
                        moduleInst.FuncAddrs.Add(funcAddr);
                        break;
                    default: break;
                }
            }

            int idx = module.ImportedFunctions.Count;
            foreach (var fakeFunc in module.Funcs)
            {
                var addr = store.AllocateWasmFunction(fakeFunc, moduleInst);
                moduleInst.FuncAddrs.Add(addr);
                var func = store[addr];
                if (string.IsNullOrEmpty(func.Id))
                    func.SetName($"{idx} {func.Id}");
                idx++;
            }

            foreach (var export in module.Exports)
            {
                if (export.Desc is Module.ExportDesc.FuncDesc desc)
                {
                    var addr = moduleInst.FuncAddrs[desc.FunctionIndex];
                    var funcInst = store[addr];
                    funcInst.SetName(export.Name);
                }
            }

            var ftype = Types[modFunc.TypeIndex].Expansion;
            var funcType = ftype as FunctionType;
            if (funcType is null)
                throw new InvalidDataException("Function had invalid type:{ftype}");
            
            var dummyContext = new ExecContext(store, new RuntimeAttributes { Live = false } );
            var execFrame = dummyContext.ReserveFrame(
                ModuleInst, 
                new FunctionType(ResultType.Empty, funcType.ResultType), 
                modFunc.Index,
                modFunc.Locals);
            
            dummyContext.PushFrame(execFrame);
            return dummyContext;
        }

        public void NewOpStack(ResultType parameters) {}

        public void FreeOpStack(ResultType results) {}

        public void Push(ValType type)
        {
            LastEvent += ">";
            switch (type)
            {
                case ValType.I32: fakeStack.Push("I"); break;
                case ValType.I64: fakeStack.Push("L"); break;
                case ValType.F32: fakeStack.Push("F"); break;
                case ValType.F64: fakeStack.Push("D"); break;
                case ValType.V128: fakeStack.Push("V"); break;
                case ValType.FuncRef: fakeStack.Push("R"); break;
                case ValType.ExternRef: fakeStack.Push("E"); break;
                case ValType.Nil: fakeStack.Push("N"); break;
                case ValType.ExecContext: fakeStack.Push("|"); break;
            }
        }

        public Value Pop(ValType type)
        {
            if (type == ValType.ExecContext)
            {
                while (fakeStack.Count > 0)
                {
                    string top = fakeStack.Pop();
                    if (top == "|")
                        break;
                    
                    LastEvent += "x";
                }
                return Value.NullFuncRef;
            }
            
            if (fakeStack.Count == 0)
                return Value.NullFuncRef;

            
            string op = fakeStack.Pop();
            if (op == "|")
                LastEvent += "{=U=}";
            else
                LastEvent += "<";
            return new Value(type);
        }

        public string GetString()
        {
            return string.Join("", fakeStack.ToArray().Reverse()) + " " + LastEvent;
        }

        delegate void FakeHostDelegate();
    }
    
    public class StackRenderer
    {
        public StackRenderer(StackRenderer? parent, bool doesWrite, int width = 40, FakeContext? context = null)
        {
            Parent = parent;
            DoesWrite = doesWrite;
            Width = width;
            FakeContext = context ?? parent!.FakeContext;
        }

        public FakeContext FakeContext { get; }

        private StackRenderer? Parent { get; }
        public bool DoesWrite { get; }
        public int Width { get; }

        public StackRenderer SubRenderer() => new(this, DoesWrite, Width);

        public void ProcessInstruction(InstructionBase inst)
        {
            FakeContext.LastEvent = "";
            inst.Validate(FakeContext);
        }

        public override string ToString()
        {
            if (!DoesWrite)
                return "";
            
            return $"(;{FakeContext.GetString()};)".PadRight(Width, ' ');
        }
    }
}