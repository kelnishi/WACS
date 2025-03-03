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
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Wacs.Compilation
{
    [Generator]
    public class OpSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new MethodSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // Retrieve the syntax receiver
            if (context.SyntaxReceiver is not MethodSyntaxReceiver receiver)
                return;

            foreach (var method in receiver.CandidateMethods)
            {
                var semanticModel = context.Compilation.GetSemanticModel(method.SyntaxTree);
                var methodSymbol = ModelExtensions.GetDeclaredSymbol(semanticModel, method) as IMethodSymbol;

                // Look for our [StoreSource] attribute by checking the attribute names
                if (methodSymbol?.GetAttributes().Any(ad => ad.AttributeClass?.Name == "OpSourceAttribute") ?? false)
                {
                    SyntaxNode methodBody;
                    if (method.Body != null)
                    {
                        methodBody = method.Body;
                    }
                    else if (method.ExpressionBody != null)
                    {
                        methodBody = method.ExpressionBody.Expression;
                    }
                    else
                    {
                        throw new InvalidOperationException("Method does not have a body.");
                    }
                    
                    // Rewrite the method and capture locals
                    var rewriter = new ParameterRewriter(semanticModel);
                    rewriter.Visit(method);
                    string newBodyText = rewriter.Visit(methodBody).ToString().Replace("\"", "\"\"");
                    
                    string template = $@"@""{newBodyText}"";";

                    SyntaxList<AttributeListSyntax> attributesList = method.AttributeLists;
                    foreach (var kv in rewriter.SymbolAnnotations)
                    {
                        List<AttributeArgumentSyntax> parameters = new();
                        parameters.Add(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    SyntaxFactory.Literal(kv.Key)
                                )
                            )
                        );
                        parameters.Add(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.StringLiteralExpression, 
                                    SyntaxFactory.Literal(kv.Value.name.Trim())))
                        );

                        var newAttribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName(kv.Value.isparam?"OpParam":"OpLocal"))
                            .WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(parameters)));
                        var newAttributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(newAttribute));

                        attributesList = attributesList.Add(newAttributeList);
                    }

                    switch (method.ReturnType.ToFullString())
                    {
                        case "void":
                            break;
                        default:
                            List<AttributeArgumentSyntax> parameters = new();
                            parameters.Add(
                                SyntaxFactory.AttributeArgument(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression, 
                                        SyntaxFactory.Literal(method.ReturnType.ToFullString().Trim())))
                            );

                            var newAttribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName("OpReturn"))
                                .WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(parameters)));
                            var newAttributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(newAttribute));

                            attributesList = attributesList.Add(newAttributeList);
                            break;
                    }
                    
                    
                    var methodDeclaration = SyntaxFactory.MethodDeclaration(
                            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                            methodSymbol.Name
                        )
                        .AddModifiers(
                            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                            SyntaxFactory.Token(SyntaxKind.StaticKeyword)
                        )
                        .WithAttributeLists(attributesList)
                        .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.ParseExpression(template)));
                    
                    var classDeclaration = SyntaxFactory.ClassDeclaration("InstructionSource")
                        .AddModifiers(
                            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                            SyntaxFactory.Token(SyntaxKind.PartialKeyword)
                        )
                        .AddMembers(methodDeclaration);
                    
                    var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName("Wacs.Core.Compilation"))
                        .AddMembers(classDeclaration);
                    
                    var compilationUnit = SyntaxFactory.CompilationUnit()
                        .AddMembers(namespaceDeclaration)
                        .AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Wacs.Core.OpCodes")))
                        .NormalizeWhitespace();
                    
                    context.AddSource($"{methodSymbol.Name}.g.cs", SourceText.From(compilationUnit.ToFullString(), Encoding.UTF8));
                }
            }
        }

        /// <summary>
        /// A simple syntax receiver that collects method declarations with attributes.
        /// </summary>
        private class MethodSyntaxReceiver : ISyntaxReceiver
        {
            public List<MethodDeclarationSyntax> CandidateMethods { get; } = new List<MethodDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is MethodDeclarationSyntax methodDecl && methodDecl.AttributeLists.Count > 0)
                {
                    CandidateMethods.Add(methodDecl);
                }
            }
        }
    }
}