using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests
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
            using var writer = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            writer.WriteLine($"::error title=MSTest failure::{name}");
        }
    }
}
