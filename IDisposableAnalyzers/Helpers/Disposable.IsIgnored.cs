namespace IDisposableAnalyzers
{
    using System.Threading;
    using Gu.Roslyn.AnalyzerExtensions;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal static partial class Disposable
    {
        internal static bool IsIgnored(ExpressionSyntax node, SemanticModel semanticModel, CancellationToken cancellationToken, PooledSet<SyntaxNode> visited = null)
        {
            if (node.Parent is EqualsValueClauseSyntax equalsValueClause)
            {
                if (equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator &&
                    variableDeclarator.Identifier.Text == "_")
                {
                    return true;
                }

                return false;
            }

            if (node.Parent is AssignmentExpressionSyntax assignmentExpression)
            {
                return assignmentExpression.Left is IdentifierNameSyntax identifierName &&
                       identifierName.Identifier.Text == "_";
            }

            if (node.Parent is AnonymousFunctionExpressionSyntax ||
                node.Parent is UsingStatementSyntax ||
                node.Parent is ReturnStatementSyntax ||
                node.Parent is ArrowExpressionClauseSyntax)
            {
                return false;
            }

            if (node.Parent is StatementSyntax)
            {
                return true;
            }

            if (node.Parent is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax left &&
                left.Identifier.ValueText == "_")
            {
                return true;
            }

            if (node.Parent is ArgumentSyntax argument)
            {
                return IsIgnored(argument, semanticModel, cancellationToken, visited);
            }

            if (node.Parent is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Parent is InvocationExpressionSyntax invocation &&
                    DisposeCall.IsIDisposableDispose(invocation, semanticModel, cancellationToken))
                {
                    return false;
                }

                return IsChainedDisposingInReturnValue(memberAccess, semanticModel, cancellationToken, visited).IsEither(Result.No, Result.AssumeNo);
            }

            if (node.Parent is ConditionalAccessExpressionSyntax conditionalAccess)
            {
                if (conditionalAccess.WhenNotNull is InvocationExpressionSyntax invocation &&
                    DisposeCall.IsIDisposableDispose(invocation, semanticModel, cancellationToken))
                {
                    return false;
                }

                return IsChainedDisposingInReturnValue(conditionalAccess, semanticModel, cancellationToken, visited).IsEither(Result.No, Result.AssumeNo);
            }

            if (node.Parent is InitializerExpressionSyntax initializer &&
                initializer.Parent is ExpressionSyntax creation)
            {
#pragma warning disable IDISP003 // Dispose previous before re - assigning.
                using (visited = visited.IncrementUsage())
#pragma warning restore IDISP003
                {
                    if (visited.Add(creation))
                    {
                        return IsIgnored(creation, semanticModel, cancellationToken, visited);
                    }

                    return false;
                }
            }

            return false;
        }

        internal static bool IsIgnored(ArgumentSyntax argument, SemanticModel semanticModel, CancellationToken cancellationToken, PooledSet<SyntaxNode> visited)
        {
            if (argument != null &&
                argument.Parent is ArgumentListSyntax argumentList &&
                argumentList.Parent is ExpressionSyntax parentExpression &&
                semanticModel.TryGetSymbol(parentExpression, cancellationToken, out IMethodSymbol method))
            {
                if (method == KnownSymbol.CompositeDisposable.Add)
                {
                    return false;
                }

                if (method.Name == "Add" &&
                    method.ContainingType.IsAssignableTo(KnownSymbol.IEnumerable, semanticModel.Compilation))
                {
                    return false;
                }

                switch (IsDisposedByReturnValue(argument, semanticModel, cancellationToken))
                {
                    case Result.Yes:
                    case Result.AssumeYes:
#pragma warning disable IDISP003 // Dispose previous before re - assigning.
                        using (visited = visited.IncrementUsage())
#pragma warning restore IDISP003
                        {
                            if (visited.Add(parentExpression))
                            {
                                return IsIgnored(parentExpression, semanticModel, cancellationToken, visited);
                            }

                            return false;
                        }
                }

                if (TryGetAssignedFieldOrProperty(argument, method, semanticModel, cancellationToken, out var fieldOrProperty) &&
                    IsAssignableFrom(fieldOrProperty.Type, semanticModel.Compilation))
                {
                    switch (parentExpression.Parent.Kind())
                    {
                        case SyntaxKind.ArrowExpressionClause:
                        case SyntaxKind.ReturnStatement:
                            return true;
                        case SyntaxKind.EqualsValueClause:
                            return parentExpression.Parent.Parent.IsKind(SyntaxKind.VariableDeclarator) &&
                                   !semanticModel.IsAccessible(argument.SpanStart, fieldOrProperty.Symbol);
                        case SyntaxKind.SimpleAssignmentExpression:
                            return !semanticModel.IsAccessible(argument.SpanStart, fieldOrProperty.Symbol);
                    }

                    return false;
                }

                return IsAssignedToDisposable(argument, semanticModel, cancellationToken, visited).IsEither(Result.No, Result.AssumeNo);
            }

            return false;
        }

        private static Result IsChainedDisposingInReturnValue(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel, CancellationToken cancellationToken, PooledSet<SyntaxNode> visited)
        {
            if (semanticModel.TryGetSymbol(memberAccess, cancellationToken, out ISymbol symbol))
            {
                return IsChainedDisposingInReturnValue(symbol, memberAccess, semanticModel, cancellationToken, visited);
            }

            return Result.Unknown;
        }

        private static Result IsChainedDisposingInReturnValue(ConditionalAccessExpressionSyntax conditionalAccess, SemanticModel semanticModel, CancellationToken cancellationToken, PooledSet<SyntaxNode> visited)
        {
            if (semanticModel.TryGetSymbol(conditionalAccess.WhenNotNull, cancellationToken, out ISymbol symbol))
            {
                return IsChainedDisposingInReturnValue(symbol, conditionalAccess, semanticModel, cancellationToken, visited);
            }

            return Result.Unknown;
        }

        private static Result IsChainedDisposingInReturnValue(ISymbol symbol, ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken, PooledSet<SyntaxNode> visited)
        {
            if (symbol is IMethodSymbol method)
            {
                if (method.ReturnsVoid)
                {
                    return Result.No;
                }

                if (method.ReturnType.Name == "ConfiguredTaskAwaitable")
                {
                    return Result.Yes;
                }

                if (method.ContainingType.DeclaringSyntaxReferences.Length == 0)
                {
                    if (method.ReturnType == KnownSymbol.Task)
                    {
                        return Result.No;
                    }

                    if (method.ReturnType == KnownSymbol.TaskOfT &&
                        method.ReturnType is INamedTypeSymbol namedType &&
                        namedType.TypeArguments.TrySingle(out var type))
                    {
                        return !IsAssignableFrom(type, semanticModel.Compilation)
                            ? Result.No
                            : Result.AssumeYes;
                    }

                    return !IsAssignableFrom(method.ReturnType, semanticModel.Compilation)
                        ? Result.No
                        : Result.AssumeYes;
                }

                if (method.IsExtensionMethod &&
                    method.ReducedFrom is IMethodSymbol reducedFrom &&
                    reducedFrom.Parameters.TryFirst(out var parameter))
                {
                    return IsDisposedByReturnValue(parameter, expression.Parent, semanticModel, cancellationToken, visited);
                }
            }

            return Result.AssumeNo;
        }
    }
}
