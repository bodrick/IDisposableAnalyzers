﻿namespace IDisposableAnalyzers.Test
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Gu.Roslyn.Asserts;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    using NUnit.Framework;

    [Ignore("For harvesting test cases only.")]
    public static class ReproBox
    {
        // ReSharper disable once UnusedMember.Local
        private static readonly IReadOnlyList<DiagnosticAnalyzer> AllAnalyzers =
            typeof(KnownSymbols)
                .Assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t))
                .ToArray();

        private static readonly Solution Solution = CodeFactory.CreateSolution(
            new FileInfo("C:\\Git\\_GuOrg\\Gu.Reactive\\Gu.Reactive.sln"));

        [TestCaseSource(nameof(AllAnalyzers))]
        public static void SolutionRepro(DiagnosticAnalyzer analyzer)
        {
            RoslynAssert.Valid(analyzer, Solution);
        }

        [TestCaseSource(nameof(AllAnalyzers))]
        public static void Repro(DiagnosticAnalyzer analyzer)
        {
            var code = @"
namespace N
{
    public sealed class C
    {
    }
}";
            RoslynAssert.Valid(analyzer, code);
        }
    }
}
