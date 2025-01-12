﻿namespace IDisposableAnalyzers.Test
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Gu.Roslyn.Asserts;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;

    public static class Recursion
    {
        private static readonly IReadOnlyList<DiagnosticAnalyzer> AllAnalyzers =
            typeof(AnalyzerCategory)
                .Assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t))
                .ToArray();

        [Test]
        public static void NotEmpty()
        {
            CollectionAssert.IsNotEmpty(AllAnalyzers);
        }

        [TestCaseSource(nameof(AllAnalyzers))]
        public static void ConstructorCallingSelf(DiagnosticAnalyzer analyzer)
        {
            var code = @"
namespace N
{
    using System;

    public sealed class C : IDisposable
    {
        private IDisposable disposable;

        public C()
            : this()
        {
        }

        public C(IDisposable disposable)
            : this(IDisposable disposable)
        {
            this.disposable = disposable;
        }

        public C(int i, IDisposable disposable)
            : this(disposable, i)
        {
            this.disposable = disposable;
        }

        public C(IDisposable disposable, int i)
            : this(i, disposable)
        {
            this.disposable = disposable;
        }

        public void Dispose()
        {
        }

        public static C Create(string fileName) => new C(File.OpenRead(fileName));
    }
}";
            RoslynAssert.NoAnalyzerDiagnostics(analyzer, code);
        }

        [TestCaseSource(nameof(AllAnalyzers))]
        public static void ConstructorCycle(DiagnosticAnalyzer analyzer)
        {
            var code = @"
namespace N
{
    using System;

    public sealed class C : IDisposable
    {
        private IDisposable disposable;

        public C(int i, IDisposable disposable)
            : this(disposable, i)
        {
            this.disposable = disposable;
        }

        public C(IDisposable disposable, int i)
            : this(i, disposable)
        {
            this.disposable = disposable;
        }

        public void Dispose()
        {
        }
    }
}";
            RoslynAssert.NoAnalyzerDiagnostics(analyzer, code);
        }
    }
}
