﻿namespace IDisposableAnalyzers.Test.IDISP010CallBaseDisposeTests
{
    using Gu.Roslyn.Asserts;
    using NUnit.Framework;

    public static class CodeFix
    {
        private static readonly DisposeMethodAnalyzer Analyzer = new();
        private static readonly AddBaseCallFix Fix = new();
        private static readonly ExpectedDiagnostic ExpectedDiagnostic = ExpectedDiagnostic.Create(Descriptors.IDISP010CallBaseDispose);

        private const string DisposableCode = @"
namespace N
{
    using System;

    public class Disposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}";

        [Test]
        public static void WhenNotCallingBaseDisposeWithBaseCode()
        {
            var baseClass = @"
namespace N
{
    using System;

    public abstract class BaseClass : IDisposable
    {
        private readonly IDisposable disposable = new Disposable();
        private bool disposed;

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            if (disposing)
            {
                this.disposable.Dispose();
            }
        }
    }
}";
            var before = @"
namespace N
{
    public class C : BaseClass
    {
        protected override void ↓Dispose(bool disposing)
        {
        }
    }
}";

            var after = @"
namespace N
{
    public class C : BaseClass
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}";
            RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, new[] { DisposableCode, baseClass, before }, after);
            RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, new[] { DisposableCode, baseClass, before }, after);
        }

        [Test]
        public static void WhenNotCallingBaseDisposeWithoutBaseCode()
        {
            var before = @"
namespace N
{
    using System.IO;

    public class C : StreamReader
    {
        public C(Stream stream)
            : base(stream)
        {
        }

        protected override void ↓Dispose(bool disposing)
        {
        }
    }
}";

            var after = @"
namespace N
{
    using System.IO;

    public class C : StreamReader
    {
        public C(Stream stream)
            : base(stream)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}";
            RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, new[] { DisposableCode, before }, after);
            RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, new[] { DisposableCode, before }, after);
        }

        [Test]
        public static void WhenNotCallingOverriddenDispose()
        {
            var baseClass = @"
namespace N
{
    using System;

    public abstract class BaseClass : IDisposable
    {
        private readonly IDisposable disposable = new Disposable();
        private bool disposed;

        /// <inheritdoc/>
        public virtual void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.disposable.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}";

            var before = @"
namespace N
{
    public class C : BaseClass
    {
        public override void ↓Dispose()
        {
        }
    }
}";

            var after = @"
namespace N
{
    public class C : BaseClass
    {
        public override void Dispose()
        {
            base.Dispose();
        }
    }
}";

            RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, new[] { DisposableCode, baseClass, before }, after);
            RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, new[] { DisposableCode, baseClass, before }, after);
        }
    }
}
