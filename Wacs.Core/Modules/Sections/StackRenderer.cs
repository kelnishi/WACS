using System;
using System.Collections.Generic;
using System.Linq;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
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

        public void Push(ResultType types)
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
        public Value PopI32() => _context.Pop(ValType.I32);
        public Value PopI64() => _context.Pop(ValType.I64);
        public Value PopF32() => _context.Pop(ValType.F32);
        public Value PopF64() => _context.Pop(ValType.F64);
        public Value PopV128() => _context.Pop(ValType.V128);
        public Value PopRefType() => _context.Pop(ValType.Funcref);

        public void ValidateStack(ResultType types, bool keep = true)
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

        public Value PopType(ValType type) => _context.Pop(type);
        public Value PopAny() => _context.Pop(ValType.Nil);
    }
    
    public class FakeContext : IWasmValidationContext
    {
        private Module _module;
        private FakeOpStack _opStack;

        public Stack<string> fakeStack = new();

        public string lastEvent = "";

        public FakeContext(Module module, Module.Function func)
        {
            ValidationModule = new ModuleInstance(module);
            _module = module;

            Types = new TypesSpace(module);
            Funcs = new FunctionsSpace(module);
            Tables = new TablesSpace(module);
            Mems = new MemSpace(module);

            Elements = new ElementsSpace(module.Elements.ToList());
            Datas = new DataValidationSpace(module.Datas.Length);

            Globals = new GlobalValidationSpace(module);

            _opStack = new FakeOpStack(this);
            
            PushFrame(func);
        }

        private ModuleInstance ValidationModule { get; }
        public IValidationOpStack OpStack => _opStack;

        public void Assert(bool factIsTrue, WasmValidationContext.MessageProducer message) {}

        public ValidationControlStack ControlStack { get; } = new();

        public void NewOpStack(ResultType parameters) {}

        public void ValidateBlock(Block instructionBlock, int index = 0) { }

        public bool Reachability { get; set; }

        public void FreeOpStack(ResultType results) {}

        public IValidationOpStack ReturnStack => _opStack;

        public TypesSpace Types { get; }
        public FunctionsSpace Funcs { get; }
        public TablesSpace Tables { get; }
        public MemSpace Mems { get; }
        public GlobalValidationSpace Globals { get; }
        public LocalsSpace Locals => ControlStack.Frame.Locals;
        public ElementsSpace Elements { get; set; }
        public DataValidationSpace Datas { get; set; }

        private void PushFrame(Module.Function func)
        {
            var funcType = Types[func.TypeIndex];
            var locals = new LocalsSpace(funcType.ParameterTypes.Types, func.Locals);
            var frame = new Frame(ValidationModule, funcType) { Locals = locals };
            ControlStack.PushFrame(frame);
        }

        public void Push(ValType type)
        {
            lastEvent += ">";
            switch (type)
            {
                case ValType.I32: fakeStack.Push("I"); break;
                case ValType.I64: fakeStack.Push("L"); break;
                case ValType.F32: fakeStack.Push("F"); break;
                case ValType.F64: fakeStack.Push("D"); break;
                case ValType.V128: fakeStack.Push("V"); break;
                case ValType.Funcref: fakeStack.Push("R"); break;
                case ValType.Externref: fakeStack.Push("E"); break;
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
                    
                    lastEvent += "x";
                }
                return Value.NullFuncRef;
            }
            
            if (fakeStack.Count == 0)
                return Value.NullFuncRef;

            
            string op = fakeStack.Pop();
            if (op == "|")
                lastEvent += "{=U=}";
            else
                lastEvent += "<";
            return new Value(type);
        }

        public string GetString()
        {
            return string.Join("", fakeStack.ToArray().Reverse()) + " " + lastEvent;
        }
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

        public void ProcessBlockInstruction(IBlockInstruction inst)
        {
            var funcType = FakeContext.Types.ResolveBlockType(inst.Type) ?? new FunctionType(ResultType.Empty,ResultType.Empty);
            var label = new Label(funcType.ResultType, new InstructionPointer(), OpCode.Block);
            FakeContext.ControlStack.Frame.Labels.Push(label);
            FakeContext.OpStack.ValidateStack(funcType.ParameterTypes, false);
            FakeContext.Push(ValType.ExecContext);
            FakeContext.OpStack.Push(funcType.ParameterTypes);
        }

        public void ElseBlockInstruction(IBlockInstruction inst)
        {
            var funcType = FakeContext.Types.ResolveBlockType(inst.Type) ?? new FunctionType(ResultType.Empty,ResultType.Empty);
            FakeContext.lastEvent = "]";
            FakeContext.ControlStack.Frame.Labels.Pop();
            FakeContext.OpStack.ValidateStack(funcType.ResultType, false);
            FakeContext.Pop(ValType.ExecContext);
        }

        public void EndBlockInstruction(IBlockInstruction inst)
        {
            var funcType = FakeContext.Types.ResolveBlockType(inst.Type) ?? new FunctionType(ResultType.Empty,ResultType.Empty);
            FakeContext.lastEvent = "]";
            FakeContext.ControlStack.Frame.Labels.Pop();
            FakeContext.OpStack.ValidateStack(funcType.ResultType, false);
            FakeContext.Pop(ValType.ExecContext);
            FakeContext.OpStack.Push(funcType.ResultType);
        }

        public void ProcessInstruction(IInstruction inst)
        {
            FakeContext.lastEvent = "";
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