﻿namespace IDisposableAnalyzers
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading;
    using Gu.Roslyn.AnalyzerExtensions;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal sealed partial class DisposableWalker
    {
        internal static bool DisposedByReturnValue(ArgumentSyntax candidate, SemanticModel semanticModel, CancellationToken cancellationToken, [NotNullWhen(true)] out ExpressionSyntax? creation)
        {
            using var recursion = Recursion.Borrow(semanticModel, cancellationToken);
            if (recursion.Target(candidate) is { } target)
            {
                return DisposedByReturnValue(target, recursion, out creation);
            }

            creation = null;
            return false;
        }

        private static bool DisposedByReturnValue(ExpressionSyntax candidate, Recursion recursion, [NotNullWhen(true)] out ExpressionSyntax? creation)
        {
            switch (candidate.Parent.Kind())
            {
                case SyntaxKind.CastExpression:
                case SyntaxKind.AsExpression:
                case SyntaxKind.ConditionalExpression:
                case SyntaxKind.CoalesceExpression:
                    return DisposedByReturnValue((ExpressionSyntax)candidate.Parent, recursion, out creation);
            }

            switch (candidate.Parent)
            {
                case ArgumentSyntax argument
                    when recursion.Target(argument) is { } target:
                    return DisposedByReturnValue(target, recursion, out creation);
                case InitializerExpressionSyntax { Parent: ObjectCreationExpressionSyntax objectCreation }
                    when recursion.SemanticModel.TryGetType(objectCreation, recursion.CancellationToken, out var type) &&
                         type == KnownSymbol.CompositeDisposable:
                    creation = objectCreation;
                    return true;
                default:
                    creation = null;
                    return false;
            }
        }

        private static bool DisposedByReturnValue(Target<ArgumentSyntax, IParameterSymbol, BaseMethodDeclarationSyntax> target, Recursion recursion, [NotNullWhen(true)] out ExpressionSyntax? creation)
        {
            switch (target)
            {
                case { Symbol: { ContainingSymbol: IMethodSymbol constructor } parameter, Source: { Parent: ArgumentListSyntax { Parent: ObjectCreationExpressionSyntax { Type: { } type } objectCreation } } }:
                    if (type == KnownSymbol.SingleAssignmentDisposable ||
                        type == KnownSymbol.RxDisposable ||
                        type == KnownSymbol.CompositeDisposable)
                    {
                        creation = objectCreation;
                        return true;
                    }

                    if (Disposable.IsAssignableFrom(target.Symbol.ContainingType, recursion.SemanticModel.Compilation))
                    {
                        if (constructor.ContainingType == KnownSymbol.BinaryReader ||
                            constructor.ContainingType == KnownSymbol.BinaryWriter ||
                            constructor.ContainingType == KnownSymbol.StreamReader ||
                            constructor.ContainingType == KnownSymbol.StreamWriter ||
                            constructor.ContainingType == KnownSymbol.CryptoStream ||
                            constructor.ContainingType == KnownSymbol.DeflateStream ||
                            constructor.ContainingType == KnownSymbol.GZipStream ||
                            constructor.ContainingType == KnownSymbol.StreamMemoryBlockProvider)
                        {
                            if (constructor.TryFindParameter("leaveOpen", out var leaveOpenParameter) &&
                                objectCreation.TryFindArgument(leaveOpenParameter, out var leaveOpenArgument) &&
                                leaveOpenArgument.Expression is LiteralExpressionSyntax literal &&
                                literal.IsKind(SyntaxKind.TrueLiteralExpression))
                            {
                                creation = null;
                                return false;
                            }

                            creation = objectCreation;
                            return true;
                        }

                        if (parameter.Type.IsAssignableTo(KnownSymbol.HttpMessageHandler, recursion.SemanticModel.Compilation) &&
                            constructor.ContainingType.IsAssignableTo(KnownSymbol.HttpClient, recursion.SemanticModel.Compilation))
                        {
                            if (constructor.TryFindParameter("disposeHandler", out var leaveOpenParameter) &&
                                objectCreation.TryFindArgument(leaveOpenParameter, out var leaveOpenArgument) &&
                                leaveOpenArgument.Expression is LiteralExpressionSyntax literal &&
                                literal.IsKind(SyntaxKind.FalseLiteralExpression))
                            {
                                creation = null;
                                return false;
                            }

                            creation = objectCreation;
                            return true;
                        }

                        if (DisposedByReturnValue(target, recursion))
                        {
                            creation = objectCreation;
                            return true;
                        }
                    }

                    break;
                case { Symbol: { ContainingSymbol: IMethodSymbol method } parameter, Source: { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } } }:
                    if (method == KnownSymbol.Task.FromResult)
                    {
                        creation = invocation;
                        return true;
                    }

                    if (Disposable.IsAssignableFrom(method.ReturnType, recursion.SemanticModel.Compilation) &&
                        DisposedByReturnValue(target, recursion))
                    {
                        creation = invocation;
                        return true;
                    }

                    break;
            }

            creation = null;
            return false;
        }

        [Obsolete("Merge with above.")]
        private static bool DisposedByReturnValue<TSource>(Target<TSource, IParameterSymbol, BaseMethodDeclarationSyntax> target, Recursion recursion)
            where TSource : SyntaxNode
        {
            using (var walker = CreateUsagesWalker(target, recursion))
            {
                foreach (var usage in walker.usages)
                {
                    switch (usage.Parent.Kind())
                    {
                        case SyntaxKind.ReturnStatement:
                        case SyntaxKind.ArrowExpressionClause:
                            return true;
                    }

                    if (Assigns(usage, recursion, out var fieldOrProperty) &&
                        DisposableMember.IsDisposed(fieldOrProperty, target.Symbol.ContainingType, recursion.SemanticModel, recursion.CancellationToken).IsEither(Result.Yes, Result.AssumeYes))
                    {
                        return true;
                    }

                    if (usage.Parent is ArgumentSyntax argument &&
                        recursion.Target(argument) is { } argumentTarget &&
                        DisposedByReturnValue(argumentTarget, recursion, out var invocationOrObjectCreation) &&
                        Returns(invocationOrObjectCreation, recursion))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
