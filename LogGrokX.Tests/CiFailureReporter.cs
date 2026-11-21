using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class CiFailureReporter
    {
        [GlobalTestCleanup]
        public static void ReportFailure(TestContext context)
        {
            if (context.CurrentTestOutcome != UnitTestOutcome.Failed)
                return;

            var name = $"{context.FullyQualifiedTestClassName}.{context.TestName}"
                .Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
            throw new InvalidOperationException($"\n::error title=MSTest failure::{name}");
        }
    }
}
