using FluentAssertions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace DisposableFixer.Test.IssueTests
{
    [TestFixture]
    internal class If_Analyser_runs_on_code_from_issue_29 : IssueSpec
    {
        private const string Code = @"
using System.Threading;
namespace DisFixerTest.Ignored {
    class IgnoreCancellationtokenRegistration {
        public IgnoreCancellationtokenRegistration() {
            var token_marked_as_not_disposed = new CancellationToken();

            var registration_not_marked = token_marked_as_not_disposed.Register(() => { });
        }
    }
}";

        private Diagnostic[] _diagnostics;


        protected override void BecauseOf()
        {
            _diagnostics = MyHelper.RunAnalyser(Code, Sut);
        }

        [Test]
        public void Then_there_should_be_no_Diagnostics()
        {
            _diagnostics.Length.Should().Be(1);
        }
    }
}