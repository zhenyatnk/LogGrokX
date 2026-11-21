using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class CiFailureReporter
    {
        [AssemblyInitialize]
        public static void CaptureTesthostFailures(TestContext context)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var message = e.ExceptionObject?.ToString() ?? "Unknown testhost exception";
                message = message.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
                var bytes = Encoding.UTF8.GetBytes($"::error title=Testhost crash::{message}\n");
                using var stream = Console.OpenStandardError();
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            };
        }

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
