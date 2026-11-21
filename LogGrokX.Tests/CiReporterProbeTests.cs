using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class CiReporterProbeTests
    {
        [TestMethod]
        public void DeliberateFailure() => Assert.Fail("CI reporter probe");
    }
}
