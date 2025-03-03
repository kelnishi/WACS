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
        private Dictionary<BlockTarget, string> blockLabels = new();
        private int OpIdx = 0;
        private Stack<Value> OpStack = new();

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

            var statements = CompileSequence(funcInst.Body.Instructions).ToList();
            
            var result = funcInst.Type.ResultType;
            TypeSyntax returnType;
            switch (result.Arity)
            {
                case 0: returnType = SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)); break;
                case 1: returnType = SyntaxFactory.ParseTypeName(GetTypeName(result.Types[0])); break;
                default: throw new TranspilerException("Cannot generate multiple return values");
            }
            
            // Build a method declaration that contains the generated statements.
            var methodDeclaration = SyntaxFactory.MethodDeclaration(
                    returnType,
                    $"Fuction{funcInst.Index.Value}")
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
            //TODO: Use the InstructionSource pattern
            switch (inst)
            {
                case InstBlock blockOp:
                    foreach (var subinst in CompileBlock(blockOp))
                        yield return subinst;
                    break;
                case InstEnd endOp: //Ignore
                    break;
                case InstLoop loopOp:
                    foreach (var subinst in CompileLoop(loopOp))
                        yield return subinst;
                    break;
                case InstIf ifOp:
                    foreach (var subinst in CompileIf(ifOp))
                        yield return subinst;
                    break;
                case InstI32BinOp binOp:
                    yield return SyntaxFactory.ParseStatement("var b = context.OpStack.PopI32();");
                    yield return SyntaxFactory.ParseStatement("var a = context.OpStack.PopI32();");
                    yield return SyntaxFactory.ParseStatement("context.OpStack.PushI32(a + b);");
                    break;
                case InstI32RelOp relOp:
                    yield return SyntaxFactory.ParseStatement("var b = context.OpStack.PopI32();");
                    yield return SyntaxFactory.ParseStatement("var a = context.OpStack.PopI32();");
                    yield return SyntaxFactory.ParseStatement("context.OpStack.PushI32(a < b?1:0);");
                    break;
                case InstI32TestOp testOp:
                    yield return SyntaxFactory.ParseStatement("var b = context.OpStack.PopI32();");
                    yield return SyntaxFactory.ParseStatement("var a = context.OpStack.PopI32();");
                    yield return SyntaxFactory.ParseStatement("context.OpStack.PushI32(a == b?1:0);");
                    break;
                case InstI32Const constOp:
                    yield return SyntaxFactory.ParseStatement($"context.OpStack.PushI32({constOp.Value});");
                    break;
                case InstLocalGet getOp:
                    yield return SyntaxFactory.ParseStatement($"context.OpStack.Push(local{getOp.GetIndex()});");
                    break;
                case InstLocalSet setOp:
                    yield return SyntaxFactory.ParseStatement($"local{setOp.GetIndex()} = context.OpStack.Pop();");
                    break;
                case InstLocalTee teeOp:
                    yield return SyntaxFactory.ParseStatement("var a = context.OpStack.PopI32();");
                    yield return SyntaxFactory.ParseStatement($"local{teeOp.GetIndex()} = a;");
                    yield return SyntaxFactory.ParseStatement($"context.OpStack.Push(a);");
                    break;
                case InstUnreachable:
                    yield return SyntaxFactory.ParseStatement($"throw new WasmRuntimeException(\'unreachable\');");
                    break;
                default:
                    // throw new TranspilerException($"Opcode {inst.Op.GetMnemonic()} not supported");
                    yield return SyntaxFactory.ParseStatement($"throw new NotSupportedException(\"Opcode {inst.Op.GetMnemonic()} not supported.\");");
                    break;
            }
        }

        private IEnumerable<StatementSyntax> CompileBlock(InstBlock instBlock)
        {
            var label = SyntaxFactory.LabeledStatement(
                SyntaxFactory.Identifier($"block{BlockIdx++}"),
                SyntaxFactory.EmptyStatement()); 
            yield return SyntaxFactory.Block(CompileSequence(instBlock.GetBlock(0).Instructions).Concat(new []{label}));

        }

        private IEnumerable<StatementSyntax> CompileLoop(InstLoop instLoop)
        {
            yield return SyntaxFactory.LabeledStatement(
                SyntaxFactory.Identifier($"loop{BlockIdx++}"),
                SyntaxFactory.Block(CompileSequence(instLoop.GetBlock(0).Instructions)));
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