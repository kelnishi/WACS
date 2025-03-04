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
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Wacs.Core;
using Wacs.Core.Compilation;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler
{
    public class Transpiler
    {
        private int BlockIdx = 0;
        private int OpIdx = 0;
        
        private Dictionary<string, Operand> Operands = new();
        private Stack<Operand> OpStack = new();
        private Stack<BlockLabel> LabelStack = new();

        private string ReturnType = "void";

        private void AddOperand(Operand op) => Operands[op.Name] = op;

        public void TranspileFunction(Module module, FunctionInstance funcInst)
        {
            var funcType = (FunctionType)module.Types[funcInst.DefType.DefIndex.Value];

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
                    Name = $"@local{i}",
                    Type = GetTypeName(parameter),
                    IsDeclared = true
                });
                OpIdx = i+1;
            }
            for (int i = 0, l = funcInst.Locals.Length; i < l; i++)
            {
                var local = funcInst.Locals[i];
                AddOperand(new Operand
                {
                    Name = $"@local{OpIdx++}",
                    Type = GetTypeName(local),
                    IsDeclared = false
                });
            }
            
            
            var result = funcInst.Type.ResultType;
            TypeSyntax returnType;
            switch (result.Arity)
            {
                case 0: returnType = SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)); break;
                case 1: returnType = SyntaxFactory.ParseTypeName(GetTypeName(result.Types[0])); break;
                default: throw new TranspilerException("Cannot generate multiple return values");
            }
            ReturnType = returnType.ToFullString();
            
            var statements = CompileSequence(funcInst.Body.Instructions).ToList();
            
            // Build a method declaration that contains the generated statements.
            var methodDeclaration = SyntaxFactory.MethodDeclaration(
                    returnType,
                    $"Function{funcInst.Index.Value}")
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword))
                .WithParameterList(parameterList)
                .WithBody(SyntaxFactory.Block(statements));

            // Wrap the method in a class declaration.
            var classDeclaration = SyntaxFactory.ClassDeclaration("WasmExecutor")
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddMembers(methodDeclaration);

            // Place the class inside a namespace.
            var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName("CompiledWasm"))
                .AddMembers(classDeclaration);

            // Create the complete compilation unit (source file).
            var compilationUnit = SyntaxFactory.CompilationUnit()
                .AddMembers(namespaceDeclaration)
                .NormalizeWhitespace();
            
            // Return the generated C# code as a string.
            Console.WriteLine(compilationUnit.ToFullString());
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
                    var op = OpStack.Pop();
                    parameters[i] = $"{(op.IsDeclared ? op.Name : op.Value.ToLiteral())}";
                }
                var statement = string.Format(source.Template, parameters);

                if (source.Return != "void")
                {
                    string stackOp = $"stack{OpIdx++}";
                    OpStack.Push(new Operand
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
                    case InstEnd endOp:
                        if (endOp.FunctionEnd)
                        {
                            if (ReturnType != "void")
                            {
                                var op = OpStack.Pop();
                                string statement = $"return {op.Name};";
                                yield return SyntaxFactory.ParseStatement(statement);
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
                            aside.Push(LabelStack.Pop());
                        }
                        yield return SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.ParseExpression(aside.Peek().Name));
                        while (aside.Count > 0) LabelStack.Push(aside.Pop());
                    } break;
                    case InstBranchIf brIfOp:
                    {
                        Stack<BlockLabel> aside = new();
                        for (int i = 0; i <= brIfOp.Label; i++)
                        {
                            aside.Push(LabelStack.Pop());
                        }
                        var op = OpStack.Pop();
                        yield return SyntaxFactory.IfStatement(
                            SyntaxFactory.ParseExpression($"{(op.IsDeclared ? op.Name : op.Value.ToLiteral())} != 0"),
                            SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.ParseExpression(aside.Peek().Name))
                        );
                        while (aside.Count > 0) LabelStack.Push(aside.Pop());
                    } break;
                    case InstI32Const constOp:
                        OpStack.Push(new Operand
                        {
                            Name = $"stack{OpIdx++}",
                            Type = "int",
                            IsDeclared = false,
                            Value = new Value(constOp.Value)
                        });
                        break;
                    case InstLocalGet getOp:
                    {
                        var localName = $"@local{getOp.GetIndex()}";
                        var op = Operands[localName];
                        OpStack.Push(op);
                    } break;
                    case InstLocalSet setOp:
                    {
                        var op = OpStack.Pop();
                        var localName = $"@local{setOp.GetIndex()}";
                        var local = Operands[localName];
                        string statement =
                            $"{(local.IsDeclared ? "" : local.Type)} {localName} = {(op.IsDeclared ? op.Name : op.Value.ToLiteral())};";
                        local.IsDeclared = true;
                        yield return SyntaxFactory.ParseStatement(statement);
                    } break;
                    case InstLocalTee teeOp:
                    {
                        var op = OpStack.Pop();
                        var localName = $"@local{teeOp.GetIndex()}";
                        var local = Operands[localName];
                        string statement =
                            $"{(local.IsDeclared ? "" : local.Type)} {localName} = {(op.IsDeclared ? op.Name : op.Value.ToLiteral())};";
                        local.IsDeclared = true;
                        yield return SyntaxFactory.ParseStatement(statement);
                        OpStack.Push(local);
                    } break;
                    case InstUnreachable:
                        yield return SyntaxFactory.ParseStatement($"throw new WasmRuntimeException(\'unreachable\');");
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
            LabelStack.Push(blockLabel);
            
            var label = SyntaxFactory.LabeledStatement(
                SyntaxFactory.Identifier(blockLabel.Name),
                SyntaxFactory.EmptyStatement()); 
            yield return SyntaxFactory.Block(CompileSequence(instBlock.GetBlock(0).Instructions).Concat(new []{label}));

            LabelStack.Pop();
        }

        private IEnumerable<StatementSyntax> CompileLoop(InstLoop instLoop)
        {
            var blockLabel = new BlockLabel {
                Name = $"loop{BlockIdx++}", 
            };
            LabelStack.Push(blockLabel);
            
            yield return SyntaxFactory.LabeledStatement(
                SyntaxFactory.Identifier(blockLabel.Name),
                SyntaxFactory.Block(CompileSequence(instLoop.GetBlock(0).Instructions)));
            
            LabelStack.Pop();
        }

        private IEnumerable<StatementSyntax> CompileIf(InstIf instIf)
        {
            if (instIf.Count == 1)
            {
                yield return SyntaxFactory.IfStatement(
                    SyntaxFactory.ParseExpression("context.OpStack.PopI32() != 0"),
                    SyntaxFactory.Block(CompileSequence(instIf.GetBlock(0).Instructions))
                );
            }
            else
            {
                yield return SyntaxFactory.IfStatement(
                    SyntaxFactory.ParseExpression("context.OpStack.PopI32() != 0"),
                    SyntaxFactory.Block(CompileSequence(instIf.GetBlock(0).Instructions)),
                    SyntaxFactory.ElseClause(SyntaxFactory.Block(CompileSequence(instIf.GetBlock(1).Instructions)))
                );
            }
        }

        private static string GetTypeName(ValType type)
        {
            return type switch
            {
                ValType.I32 => "int",
                ValType.I64 => "long",
                ValType.F32 => "float",
                ValType.F64 => "double",
            };
        }
    }
}