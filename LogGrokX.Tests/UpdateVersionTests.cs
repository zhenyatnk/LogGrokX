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

        [TestMethod]
        public void CheckIsDueOnFirstRunInNewDay()
        {
            var now = new System.DateTime(2026, 9, 25, 12, 0, 0, System.DateTimeKind.Utc);
            Assert.IsTrue(UpdateCheckService.IsCheckDue(null, now));

            var morning = new System.DateTime(2026, 9, 25, 9, 0, 0);
            Assert.IsFalse(UpdateCheckService.IsCheckDueLocal(morning, morning.AddHours(14)));
            Assert.IsTrue(UpdateCheckService.IsCheckDueLocal(morning.AddHours(14), morning.AddHours(15)));
            Assert.IsTrue(UpdateCheckService.IsCheckDueLocal(morning, morning.AddDays(1).AddHours(-8)));
            Assert.IsTrue(UpdateCheckService.IsCheckDueLocal(morning.AddDays(1), morning));
        }

        [TestMethod]
        public void FindChecksumMatchesFileName()
        {
            var sums = "AAA111  LogGrokX-1.3.0-x64-portable.zip\r\nBBB222  LogGrokX-1.3.0-x64-setup.exe\r\n";

            Assert.AreEqual("BBB222", UpdateCheckService.FindChecksum(sums, "LogGrokX-1.3.0-x64-setup.exe"));
            Assert.IsNull(UpdateCheckService.FindChecksum(sums, "LogGrokX-1.3.0-x86-setup.exe"));
        }

        [TestMethod]
        [DataRow("https://github.com/zhenyatnk/LogGrokX/releases/tag/v1.3.3", "v1.3.3")]
        [DataRow("https://github.com/zhenyatnk/LogGrokX/releases/tag/v1.4.0-rc%2B1", "v1.4.0-rc+1")]
        [DataRow("https://github.com/zhenyatnk/LogGrokX/releases", null)]
        [DataRow("https://github.com/zhenyatnk/LogGrokX/releases/latest", null)]
        [DataRow("https://github.com/zhenyatnk/LogGrokX/releases/tag/", null)]
        public void ParseTagFromReleaseUrl(string url, string expected)
        {
            Assert.AreEqual(expected, UpdateCheckService.ParseTagFromReleaseUrl(new System.Uri(url)));
        }

        [TestMethod]
        public void ParseTagFromReleaseUrlHandlesMissingLocation()
        {
            Assert.IsNull(UpdateCheckService.ParseTagFromReleaseUrl(null));
        }

        [TestMethod]
        public void RateLimitResetIsTakenFromHeaders()
        {
            var now = new System.DateTime(2026, 9, 28, 12, 0, 0, System.DateTimeKind.Utc);
            var reset = new System.DateTimeOffset(now.AddMinutes(37));

            using var primary = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);
            primary.Headers.Add("X-RateLimit-Remaining", "0");
            primary.Headers.Add("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.AreEqual(reset.UtcDateTime, UpdateCheckService.GetRateLimitResetUtc(primary.StatusCode, primary.Headers, now));

            using var secondary = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
            secondary.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(System.TimeSpan.FromSeconds(90));
            Assert.AreEqual(now.AddSeconds(90), UpdateCheckService.GetRateLimitResetUtc(secondary.StatusCode, secondary.Headers, now));

            using var forbidden = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);
            Assert.IsNull(UpdateCheckService.GetRateLimitResetUtc(forbidden.StatusCode, forbidden.Headers, now));

            using var notFound = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            notFound.Headers.Add("X-RateLimit-Remaining", "0");
            Assert.IsNull(UpdateCheckService.GetRateLimitResetUtc(notFound.StatusCode, notFound.Headers, now));
        }

        [TestMethod]
        public void FallbackReleaseUsesPublishedAssetNames()
        {
            var release = UpdateCheckService.CreateFallbackRelease("v1.4.0");

            Assert.AreEqual("v1.4.0", release.Tag);
            Assert.AreEqual("https://github.com/zhenyatnk/LogGrokX/releases/tag/v1.4.0", release.PageUrl);
            Assert.AreEqual(UpdateCheckService.FallbackReleaseNotes, release.Notes);
            Assert.AreEqual(
                "https://github.com/zhenyatnk/LogGrokX/releases/download/v1.4.0/LogGrokX-1.4.0-x64-setup.exe",
                release.Assets["LogGrokX-1.4.0-x64-setup.exe"]);
            Assert.AreEqual(
                "https://github.com/zhenyatnk/LogGrokX/releases/download/v1.4.0/SHA256SUMS.txt",
                release.Assets["SHA256SUMS.txt"]);
            Assert.IsNotNull(UpdateCheckService.GetInstallerAssetName(release));
        }
    }
}
