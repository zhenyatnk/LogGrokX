using System;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class CiFailureReporter
    {
        private static int _total;
        private static int _failed;

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
            Interlocked.Increment(ref _total);
            if (context.CurrentTestOutcome != UnitTestOutcome.Failed)
                return;

            Interlocked.Increment(ref _failed);
            var name = $"{context.FullyQualifiedTestClassName}.{context.TestName}"
                .Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
            throw new InvalidOperationException($"\n::error title=MSTest failure::{name}");
        }

        [AssemblyCleanup]
        public static void ReportSummary()
        {
            throw new InvalidOperationException(
                $"\n::notice title=MSTest summary::total={_total}, failed={_failed}");
        }
    }
}
