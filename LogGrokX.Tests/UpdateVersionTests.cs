using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class UpdateVersionTests
    {
        [TestMethod]
        [DataRow("v1.3.0", "1.2.0", true)]
        [DataRow("v1.2.1", "1.2", true)]
        [DataRow("v2.0", "1.10.5", true)]
        [DataRow("v1.2.0", "1.2.0", false)]
        [DataRow("v1.2.0", "1.2", false)]
        [DataRow("v1.1.0", "1.2.0", false)]
        [DataRow("v1.2.0", "2.1", false)]
        [DataRow("v1.3.0", "1.3.0-beta", false)]
        [DataRow("v1.4.0", "1.3.0-beta", true)]
        [DataRow("garbage", "1.0", false)]
        [DataRow("v1.3.0", "", false)]
        public void IsNewer(string latest, string current, bool expected)
        {
            Assert.AreEqual(expected, UpdateVersion.IsNewer(latest, current));
        }

        [TestMethod]
        public void ParseStripsPrefixAndSuffix()
        {
            Assert.AreEqual(new System.Version(1, 2, 3, 0), UpdateVersion.Parse("v1.2.3-rc1"));
        }
    }
}
