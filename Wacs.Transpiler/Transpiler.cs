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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Wacs.Core.Compilation;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Module = Wacs.Core.Module;

namespace Wacs.Transpiler
{
    public class Transpiler
    {
        private int BlockIdx = 0;
        private int OpIdx = 0;
        
        private Dictionary<string, Operand> _operands = new();
        private Stack<Operand> _opStack = new();
        private Stack<BlockLabel> _labelStack = new();

        private FunctionInstance _funcInst = null!;
        
        private string _returnType = "void";

        private void AddOperand(Operand op) => _operands[op.Name] = op;

        public void TranspileModule(WasmRuntime runtime, ModuleInstance moduleInst)
        {
            Stopwatch timer = new();
            timer.Start();
            
            var methods = new List<MethodDeclarationSyntax>();
            foreach (var funcaddr in moduleInst.FuncAddrs)
            {
                var func = runtime.GetFunction(funcaddr);
                if (func is FunctionInstance funcInst)
                {
                    var method = TranspileFunction(funcInst);
                    methods.Add(method);
                }
            }

            string namespaceName = "CompiledWasm";
            string className = "WasmExecutor";
            
            // Wrap the method in a class declaration.
            var classDeclaration = SyntaxFactory.ClassDeclaration(className)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .WithMembers(new SyntaxList<MemberDeclarationSyntax>(methods));
            
            // Place the class inside a namespace.
            var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(namespaceName))
                .AddMembers(classDeclaration);
            
            // Create the complete compilation unit (source file).
            var compilationUnit = SyntaxFactory.CompilationUnit()
                .AddUsings(
                    SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Wacs.Core.Runtime")),
                    SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Wacs.Core.Runtime.Exceptions"))
                )
                .AddMembers(namespaceDeclaration)
                .NormalizeWhitespace();
            
            
            // Return the generated C# code as a string.
            Console.WriteLine(compilationUnit.ToFullString());
            
            timer.Stop();
            Console.WriteLine($"Transpilation took {timer.ElapsedMilliseconds}ms");
            timer.Restart();
            
            string methodName = methods[0].Identifier.ToFullString();
            
            var assembly = CompileAssembly(compilationUnit);
            
            timer.Stop();
            Console.WriteLine($"Compilation took {timer.ElapsedMilliseconds}ms");
            timer.Restart();
            
            var type = assembly.GetType($"{namespaceName}.{className}");
            var instance = Activator.CreateInstance(type);
            var invoker = type.GetMethod(methodName);
            
            timer.Stop();
            Console.WriteLine($"Dynamic Load took {timer.ElapsedMilliseconds}ms");
            timer.Restart();
            
            var result = invoker.Invoke(instance, new object?[]{ null, 12 });
            timer.Stop();
            Console.WriteLine($"Invocation took {timer.ElapsedMilliseconds}ms");
            
            Console.WriteLine(result);
        }
        
        
        public MethodDeclarationSyntax TranspileFunction(FunctionInstance funcInst)
        {
            _funcInst = funcInst;
            
            var funcType = (FunctionType)funcInst.Module.Repr.Types[funcInst.DefType.DefIndex.Value];

            List<ParameterSyntax> parameters = new();
            parameters.Add(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("context"))
                    .WithType(SyntaxFactory.ParseTypeName("ExecContext"))
            );
            parameters.AddRange(funcType.ParameterTypes.Types
                .Select((vt, index) =>
                    SyntaxFactory.Parameter(SyntaxFactory.Identifier($"local{index}"))
                        .WithType(SyntaxFactory.ParseTypeName(GetTypeName(vt)))
                ));
            
            var parameterList = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters));

            for (int i = 0, l = funcType.ParameterTypes.Arity; i < l; i++)
            {
                var parameter = funcType.ParameterTypes.Types[i];
                AddOperand(new Operand
                {
                    Name = $"local{i}",
                    Type = GetTypeName(parameter),
                    IsDeclared = true
                });
                OpIdx = i+1;
            }
            
            
            var statements = new List<StatementSyntax>(); 
            
            for (int i = 0, l = funcInst.Locals.Length; i < l; i++)
            {
                var local = funcInst.Locals[i];
                var name = $"local{OpIdx++}";
                var type = GetTypeName(local);
                AddOperand(new Operand
                {
                    Name = name,
                    Type = type,
                    IsDeclared = true
                });
                statements.Add(SyntaxFactory.ParseStatement($"{type} {name};"));
            }
            
            var result = funcInst.Type.ResultType;
            TypeSyntax returnType;
            switch (result.Arity)
            {
                case 0: returnType = SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)); break;
                case 1: returnType = SyntaxFactory.ParseTypeName(GetTypeName(result.Types[0])); break;
                default: throw new TranspilerException("Cannot generate multiple return values");
            }
            _returnType = returnType.ToFullString();

            statements.AddRange(CompileSequence(funcInst.Body.Instructions));
                
            // Build a method declaration that contains the generated statements.
            var methodDeclaration = SyntaxFactory.MethodDeclaration(
                    returnType,
                    $"Function{funcInst.Index.Value}")
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword))
                .WithParameterList(parameterList)
                .WithBody(SyntaxFactory.Block(statements));
            
            return methodDeclaration;
        }
        
        public static Assembly CompileAssembly(CompilationUnitSyntax compilationUnit)
        {
            // Convert CompilationUnitSyntax to a syntax tree
            var syntaxTree = compilationUnit.SyntaxTree;
        
            // Define references: note you'll need proper paths or use MetadataReference.CreateFromFile with typeof(object).Assembly.Location, etc.
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>();
        
            // Create the compilation
            var compilation = CSharpCompilation.Create(
                "DynamicAssembly",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        
            // Emit the assembly to a memory stream
            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);
                if (!result.Success)
                {
                    // Handle compilation errors (logging, throwing exception, etc.)
                    var failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (var diagnostic in failures)
                    {
                        Console.Error.WriteLine(diagnostic.ToString());
                    }
                    throw new Exception("Compilation failed!");
                }
                ms.Seek(0, SeekOrigin.Begin);
                // Load the assembly from the memory stream
                return Assembly.Load(ms.ToArray());
            }
        }

        private IEnumerable<StatementSyntax> CompileSequence(IEnumerable<InstructionBase> seq) 
            => seq.SelectMany(CompileInstruction);
        
        
        private IEnumerable<StatementSyntax> CompileInstruction(InstructionBase inst)
        {
            var source = InstructionSource.Get(inst.Op);
            if (source is not null)
            {
                int parameterCount = source.ParameterCount;
                object?[] parameters = new object?[parameterCount];
                for (int i = parameterCount - 1; i >= 0; i--)
                {
                    var op = _opStack.Pop();
                    parameters[i] = $"{(op.IsDeclared ? op.Name : op.Value.ToLiteral())}";
                }
                var statement = string.Format(source.Template, parameters);

                if (source.Return != "void")
                {
                    string stackOp = $"stack{OpIdx++}";
                    _opStack.Push(new Operand
                    {
                        Name = stackOp,
                        Type = source.Return,
                        IsDeclared = true,
                    });
                    if (statement.Contains("return"))
                    {
                        statement = statement.Replace("return", $"{source.Return} {stackOp} = ");
                    }
                    else
                    {
                        statement = $"{source.Return} {stackOp} = ({statement});";
                    }
                }
                
                yield return SyntaxFactory.ParseStatement(statement);
            }
            else
            {
                switch (inst)
                {
                    case InstBlock blockOp:
                        foreach (var subinst in CompileBlock(blockOp))
                            yield return subinst;
                        break;
                    case InstElse: break;
                    case InstEnd endOp:
                        if (endOp.FunctionEnd)
                        {
                            if (_returnType != "void")
                            {
                                var op = _opStack.Pop();
                                string statement = $"return {op.Name};";
                                yield return SyntaxFactory.ParseStatement(statement);
                            }
                        }
                        break;
                    case InstReturn returnOp:
                    {
                        if (_returnType != "void")
                        {
                            var op = _opStack.Pop();
                            string statement = $"return {op.Name};";
                            yield return SyntaxFactory.ParseStatement(statement);
                            _opStack.Push(op);
                        }
                    }
                        break;
                    case InstLoop loopOp:
                        foreach (var subinst in CompileLoop(loopOp))
                            yield return subinst;
                        break;
                    case InstIf ifOp:
                        foreach (var subinst in CompileIf(ifOp))
                            yield return subinst;
                        break;
                    case InstBranch brOp:
                    {
                        Stack<BlockLabel> aside = new();
                        for (int i = 0; i <= brOp.Label; i++)
                        {
                            aside.Push(_labelStack.Pop());
                        }
                        yield return SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.ParseExpression(aside.Peek().Name));
                        while (aside.Count > 0) _labelStack.Push(aside.Pop());
                    } break;
                    case InstBranchIf brIfOp:
                    {
                        Stack<BlockLabel> aside = new();
                        for (int i = 0; i <= brIfOp.Label; i++)
                        {
                            aside.Push(_labelStack.Pop());
                        }
                        var op = _opStack.Pop();
                        yield return SyntaxFactory.IfStatement(
                            SyntaxFactory.ParseExpression($"{(op.IsDeclared ? op.Name : op.Value.ToLiteral())} != 0"),
                            SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.ParseExpression(aside.Peek().Name))
                        );
                        while (aside.Count > 0) _labelStack.Push(aside.Pop());
                    } break;
                    case InstI32Const constOp:
                        _opStack.Push(new Operand
                        {
                            Name = $"stack{OpIdx++}",
                            Type = "int",
                            IsDeclared = false,
                            Value = new Value(constOp.Value)
                        });
                        break;
                    case InstLocalGet getOp:
                    {
                        var localName = $"local{getOp.GetIndex()}";
                        var op = _operands[localName];
                        _opStack.Push(op);
                    } break;
                    case InstLocalSet setOp:
                    {
                        var op = _opStack.Pop();
                        var localName = $"local{setOp.GetIndex()}";
                        var local = _operands[localName];
                        string statement =
                            $"{(local.IsDeclared ? "" : local.Type)} {localName} = {(op.IsDeclared ? op.Name : op.Value.ToLiteral())};";
                        local.IsDeclared = true;
                        yield return SyntaxFactory.ParseStatement(statement);
                    } break;
                    case InstLocalTee teeOp:
                    {
                        var op = _opStack.Pop();
                        var localName = $"local{teeOp.GetIndex()}";
                        var local = _operands[localName];
                        string statement =
                            $"{(local.IsDeclared ? "" : local.Type)} {localName} = {(op.IsDeclared ? op.Name : op.Value.ToLiteral())};";
                        local.IsDeclared = true;
                        yield return SyntaxFactory.ParseStatement(statement);
                        _opStack.Push(local);
                    } break;
                    case InstUnreachable:
                        yield return SyntaxFactory.ParseStatement($"throw new WasmRuntimeException(\"unreachable\");");
                        break;
                    default:
                        // throw new TranspilerException($"Opcode {inst.Op.GetMnemonic()} not supported");
                        yield return SyntaxFactory.ParseStatement($"throw new NotSupportedException(\"Opcode {inst.Op.GetMnemonic()} not supported.\");");
                        break;
                }
            }
        }

        private IEnumerable<StatementSyntax> CompileBlock(InstBlock instBlock)
        {
            var blockLabel = new BlockLabel {
                Name = $"block{BlockIdx++}", 
            };
            _labelStack.Push(blockLabel);
            
            var blockType = _funcInst.Module.Types.ResolveBlockType(instBlock.BlockType);
            if (blockType is null)
                throw new TranspilerException($"Could not resolve blocktype");
            
            var parameters = StashBlockOperands(blockType);
            var results = new List<Operand>();
            foreach (var statement in DeclareBlockResults(blockType, results))
                yield return statement;
            
            foreach (var stackParam in parameters)
            {
                _opStack.Push(stackParam);
            }
            
            var label = SyntaxFactory.LabeledStatement(
                SyntaxFactory.Identifier(blockLabel.Name),
                SyntaxFactory.EmptyStatement()); 
            yield return SyntaxFactory.Block(CompileSequence(instBlock.GetBlock(0).Instructions).Concat(new []{label}).Concat(FillResults(results)));

            foreach (var stackResult in results)
            {
                _opStack.Push(stackResult);
            }
            
            _labelStack.Pop();
        }

        private IEnumerable<StatementSyntax> CompileLoop(InstLoop instLoop)
        {
            
            var blockLabel = new BlockLabel {
                Name = $"loop{BlockIdx++}", 
            };
            _labelStack.Push(blockLabel);
            
            var loopType = _funcInst.Module.Types.ResolveBlockType(instLoop.BlockType);
            if (loopType is null)
                throw new TranspilerException($"Could not resolve blocktype");
            
            var parameters = StashBlockOperands(loopType);
            var results = new List<Operand>();
            foreach (var statement in DeclareBlockResults(loopType, results))
                yield return statement;
            
            foreach (var stackResult in results)
            {
                _opStack.Push(stackResult);
            }
            
            yield return SyntaxFactory.LabeledStatement(
                SyntaxFactory.Identifier(blockLabel.Name),
                SyntaxFactory.Block(CompileSequence(instLoop.GetBlock(0).Instructions)));
            
            foreach (var stackResult in results)
            {
                _opStack.Push(stackResult);
            }
            
            _labelStack.Pop();
        }

        private List<Operand> StashBlockOperands(FunctionType blockType)
        {
            List<Operand> parameters = new();
            for (int i = 0; i < blockType!.ParameterTypes.Arity; i++)
            {
                var param = _opStack.Pop();
                parameters.Add(param);
            }
            return parameters;
        }

        private IEnumerable<StatementSyntax> DeclareBlockResults(FunctionType blockType, List<Operand> results)
        {
            for (int i = 0; i < blockType!.ResultType.Arity; i++)
            {
                var type = blockType.ResultType.Types[i];
                var res = new Operand
                {
                    Name = $"stack{OpIdx++}",
                    Type = GetTypeName(type),
                    IsDeclared = true
                };
                results.Add(res);
                yield return SyntaxFactory.ParseStatement($"{res.Type} {res.Name};");
            }
        }
        
        private IEnumerable<StatementSyntax> FillResults(List<Operand> results)
        {
            foreach (var stackResult in results)
            {
                var stackop = _opStack.Pop();
                string statement = $"{stackResult.Name} = {(stackop.IsDeclared ? stackop.Name : stackop.Value.ToLiteral())};";
                yield return SyntaxFactory.ParseStatement(statement);
            }
        }

        private IEnumerable<StatementSyntax> CompileIf(InstIf instIf)
        {
            var op = _opStack.Pop();

            var blockLabel = new BlockLabel {
                Name = $"if{BlockIdx++}", 
            };
            _labelStack.Push(blockLabel);

            var ifType = _funcInst.Module.Types.ResolveBlockType(instIf.BlockType);
            if (ifType is null)
                throw new TranspilerException($"Could not resolve blocktype");
            
            var parameters = StashBlockOperands(ifType);
            var results = new List<Operand>();
            foreach (var statement in DeclareBlockResults(ifType, results))
                yield return statement;
            
            if (instIf.Count == 1)
            {
                foreach (var stackParam in parameters)
                {
                    _opStack.Push(stackParam);
                }
                
                yield return SyntaxFactory.IfStatement(
                    SyntaxFactory.ParseExpression($"{(op.IsDeclared ? op.Name : op.Value.ToLiteral())} != 0"),
                    SyntaxFactory.Block(CompileSequence(instIf.GetBlock(0).Instructions).Concat(FillResults(results)))
                );
            }
            else
            {
                foreach (var stackParam in parameters)
                {
                    _opStack.Push(stackParam);
                }
                
                yield return SyntaxFactory.IfStatement(
                    SyntaxFactory.ParseExpression($"{(op.IsDeclared ? op.Name : op.Value.ToLiteral())} != 0"),
                    SyntaxFactory.Block(CompileSequence(instIf.GetBlock(0).Instructions).Concat(FillResults(results))),
                    SyntaxFactory.ElseClause(SyntaxFactory.Block(CompileSequence(instIf.GetBlock(1).Instructions).Concat(FillResults(results))))
                );
            }

            yield return SyntaxFactory.LabeledStatement(
                SyntaxFactory.Identifier(blockLabel.Name),
                SyntaxFactory.EmptyStatement());
            
            foreach (var stackResult in results)
            {
                _opStack.Push(stackResult);
            }
            
            _labelStack.Pop();
        }

        private static string GetTypeName(ValType type)
        {
            return type switch
            {
                ValType.I32 => "int",
                ValType.I64 => "long",
                ValType.F32 => "float",
                ValType.F64 => "double",
                _ => throw new TranspilerException($"Could not map wasm type {type}")
            };
        }
    }
}