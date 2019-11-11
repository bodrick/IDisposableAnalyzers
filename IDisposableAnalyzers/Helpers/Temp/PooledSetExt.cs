namespace IDisposableAnalyzers
{
    using System.Runtime.CompilerServices;
    using Gu.Roslyn.AnalyzerExtensions;
    using Microsoft.CodeAnalysis;

    internal static class PooledSetExt
    {
        internal static bool CanVisit(this PooledSet<(string Caller, SyntaxNode Node)> visited, SyntaxNode node, out PooledSet<(string Caller, SyntaxNode Node)> incremented, [CallerMemberName] string caller = null)
        {
            incremented = visited.IncrementUsage();
            return incremented.Add((caller ?? string.Empty, node));
        }
    }
}
