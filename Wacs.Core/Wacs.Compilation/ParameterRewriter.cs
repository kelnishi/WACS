using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public class ParameterRewriter : CSharpSyntaxRewriter
{
    private readonly SemanticModel _semanticModel;
    private readonly Dictionary<ISymbol, int> _symbolIndices = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<int, (string name, string type, bool isparam)> _symbolAnnotations = new();
    private int _currentIndex = 0;

    public ParameterRewriter(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
    }

    // Expose the mapping from index to annotation (e.g. "i0: int")
    public Dictionary<int, (string name, string type, bool isparam)> SymbolAnnotations => _symbolAnnotations;

    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        // First, visit the parameter list.
        var newParameterList = (ParameterListSyntax)this.Visit(node.ParameterList);

        // Then, visit the method body (or expression body if present).
        SyntaxNode newBody;
        if (node.Body != null)
        {
            newBody = Visit(node.Body);
        }
        else if (node.ExpressionBody != null)
        {
            newBody = Visit(node.ExpressionBody);
        }
        else
        {
            throw new Exception("Method did not have a body.");
        }

        // Return the updated method declaration with visited parameters and body.
        return node.WithParameterList(newParameterList)
            .WithBody(newBody as BlockSyntax)
            .WithExpressionBody(newBody as ArrowExpressionClauseSyntax);
    }
    
    public override SyntaxNode VisitParameter(ParameterSyntax node)
    {
        // Obtain the parameter symbol from the semantic model.
        var symbol = _semanticModel.GetDeclaredSymbol(node);
        if (symbol != null && symbol.Kind == SymbolKind.Parameter)
        {
            if (!_symbolIndices.TryGetValue(symbol, out int index))
            {
                index = _currentIndex++;
                _symbolIndices[symbol] = index;
                _symbolAnnotations[index] = symbol switch
                {
                    IParameterSymbol parameter => (symbol.Name, parameter.Type.ToDisplayString(), true),
                    _ => _symbolAnnotations[index]
                };
            }
            // Replace the identifier with the numeric placeholder.
            var newIdentifier = SyntaxFactory.Identifier("{" + index + "}");
            return node.WithIdentifier(newIdentifier)
                .WithTriviaFrom(node);
        }
        return base.VisitParameter(node)!;
    }
    
    // Replace references to parameters or local variables.
    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol != null && (symbol.Kind == SymbolKind.Parameter || symbol.Kind == SymbolKind.Local))
        {
            if (!_symbolIndices.TryGetValue(symbol, out int index))
            {
                index = _currentIndex++;
                _symbolIndices[symbol] = index;
                _symbolAnnotations[index] = symbol switch
                {
                    IParameterSymbol parameter => (symbol.Name, parameter.Type.ToDisplayString(), true),
                    ILocalSymbol local => (symbol.Name, local.Type.ToDisplayString(), false),
                    _ => _symbolAnnotations[index]
                };
            }
            // Replace with a numeric placeholder for string.Format.
            string placeholder = "{" + index + "}";
            return SyntaxFactory.IdentifierName(placeholder)
                                .WithTriviaFrom(node);
        }
        return base.VisitIdentifierName(node);
    }

    // Also replace the declared identifier in local variable declarations.
    public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        var visitedNode = base.VisitVariableDeclarator(node);
        
        // Get the declared symbol for this variable.
        var symbol = _semanticModel.GetDeclaredSymbol(node);
        if (symbol != null && symbol.Kind == SymbolKind.Local)
        {
            if (!_symbolIndices.TryGetValue(symbol, out int index))
            {
                index = _currentIndex++;
                _symbolIndices[symbol] = index;
                _symbolAnnotations[index] = symbol switch
                {
                    IParameterSymbol parameter => (symbol.Name, parameter.Type.ToDisplayString(), true),
                    ILocalSymbol local => (symbol.Name, local.Type.ToDisplayString(), false),
                    _ => _symbolAnnotations[index]
                };
            }
            // Replace the variable name with the placeholder.
            return node.WithIdentifier(SyntaxFactory.Identifier("{" + index + "}"))
                       .WithTriviaFrom(node);
        }

        return visitedNode!;
    }
}