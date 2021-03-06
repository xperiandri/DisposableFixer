using FluentAssertions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace DisposableFixer.Test.IssueTests
{
    [TestFixture]
    internal class If_Analyser_runs_on_code_from_issue_58 : IssueSpec
    {
        private const string Code = @"
using System.IO;
using System.Reactive.Disposables;
namespace SomeNamespace
{
    public class SomeSpec
    {
        public SomeSpec()
        {
            var disposables = new CompositeDisposable();
            var mem = new MemoryStream();
            disposables.Add(mem);
        }
    }
}

namespace System.Reactive.Disposables
{
    public class CompositeDisposable
    {
        public void Add(IDisposable item)
        {
        }
    }
}";

        private Diagnostic[] _diagnostics;

        protected override void BecauseOf()
        {
            _diagnostics = MyHelper.RunAnalyser(Code, Sut);
        }

        [Test]
        public void Then_there_should_be_no_Diagnostic()
        {
            _diagnostics.Length.Should().Be(0);
        }
    }
}