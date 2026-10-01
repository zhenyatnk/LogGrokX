using System;
using System.Collections.Generic;
using System.IO;
using LogGrokX.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class YamlSettingsFileTests
    {
        private string _tempFile = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            var directory = Path.Combine(Path.GetTempPath(), "LogGrokX.SettingsTests");
            Directory.CreateDirectory(directory);
            _tempFile = Path.Combine(directory, $"{Guid.NewGuid():N}.yaml");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(_tempFile))
                File.Delete(_tempFile);
        }

        [TestMethod]
        public void SetScalarReplacesValueAndKeepsComments()
        {
            WriteFile(
                "Settings:\n" +
                "  ViewSettings:\n" +
                "    # BigLine: prune|break\n" +
                "    BigLine: prune\n" +
                "    BigLineSize: 4096\n");

            var file = new YamlSettingsFile(_tempFile);
            file.SetScalar("ViewSettings", "BigLine", "break");
            file.Save();

            var text = File.ReadAllText(_tempFile);
            StringAssert.Contains(text, "# BigLine: prune|break");
            StringAssert.Contains(text, "    BigLine: break");
            StringAssert.Contains(text, "    BigLineSize: 4096");
        }

        [TestMethod]
        public void SetScalarKeepsInlineComment()
        {
            WriteFile(
                "Settings:\n" +
                "  DebugSettings:\n" +
                "    MaxDumpsCount: 10 # keep me\n");

            var file = new YamlSettingsFile(_tempFile);
            file.SetScalar("DebugSettings", "MaxDumpsCount", "20");
            file.Save();

            StringAssert.Contains(File.ReadAllText(_tempFile), "    MaxDumpsCount: 20 # keep me");
        }

        [TestMethod]
        public void SetScalarDoesNotTouchOtherSections()
        {
            WriteFile(
                "Settings:\n" +
                "  ViewSettings:\n" +
                "    BigLine: prune\n" +
                "  DebugSettings:\n" +
                "    MaxDumpsCount: 10\n");

            var file = new YamlSettingsFile(_tempFile);
            file.SetScalar("ViewSettings", "BigLine", "break");
            file.Save();

            var text = File.ReadAllText(_tempFile);
            StringAssert.Contains(text, "    BigLine: break");
            StringAssert.Contains(text, "    MaxDumpsCount: 10");
        }

        [TestMethod]
        public void ReplaceSequenceRewritesRulesAndKeepsSectionComment()
        {
            WriteFile(
                "Settings:\n" +
                "  ColorSettings:\n" +
                "    # comment before rules\n" +
                "    Rules:\n" +
                "      - RegexString: No error #use default color\n" +
                "  ViewSettings:\n" +
                "    BigLine: prune\n");

            var file = new YamlSettingsFile(_tempFile);
            file.ReplaceSequence("ColorSettings", "Rules", indent => SettingsYamlRenderer.RenderColorRules(
                new List<ColorRuleData>
                {
                    new() { RegexString = "IMP", ForegroundColor = "Red" },
                    new() { RegexString = "fatal", BackgroundColor = "#FFCD5C5C" }
                }, indent));
            file.Save();

            var text = File.ReadAllText(_tempFile);
            StringAssert.Contains(text, "# comment before rules");
            StringAssert.Contains(text, "      - RegexString: IMP");
            StringAssert.Contains(text, "        ForegroundColor: Red");
            StringAssert.Contains(text, "      - RegexString: fatal");
            StringAssert.Contains(text, "        BackgroundColor: '#FFCD5C5C'");
            StringAssert.Contains(text, "  ViewSettings:");
            StringAssert.Contains(text, "    BigLine: prune");
        }

        [TestMethod]
        public void ReplaceSequenceWithUnchangedContentKeepsInlineComments()
        {
            WriteFile(
                "Settings:\n" +
                "  ColorSettings:\n" +
                "    Rules:\n" +
                "      - RegexString: No error #use default color\n");

            var file = new YamlSettingsFile(_tempFile);
            file.ReplaceSequence("ColorSettings", "Rules", indent => SettingsYamlRenderer.RenderColorRules(
                new List<ColorRuleData>
                {
                    new() { RegexString = "No error" }
                }, indent));
            file.Save();

            Assert.IsFalse(file.HasChanges);
            StringAssert.Contains(File.ReadAllText(_tempFile), "#use default color");
        }

        [TestMethod]
        public void ReplaceSequenceRoundTripsThroughConfiguration()
        {
            WriteFile(
                "Settings:\n" +
                "  ColorSettings:\n" +
                "    Rules:\n" +
                "      - RegexString: No error\n" +
                "  ViewSettings:\n" +
                "    BigLine: prune\n" +
                "  LogFormats:\n" +
                "    - Regex: ^old$\n" +
                "      IndexedFields:\n" +
                "        - Level\n");

            const string regex = @"^(?<Time>\d{4}-\d{2}-\d{2}\s[^\s]+)\s+(?<Level>[^\s]+)";

            var file = new YamlSettingsFile(_tempFile);
            file.ReplaceSequence("ColorSettings", "Rules", indent => SettingsYamlRenderer.RenderColorRules(
                new List<ColorRuleData>
                {
                    new() { RegexString = "error: 0x0" }
                }, indent));
            file.ReplaceSequence("Settings", "LogFormats", indent => SettingsYamlRenderer.RenderLogFormats(
                new List<LogFormatData>
                {
                    new()
                    {
                        Regex = regex,
                        IndexedFields = new[] { "Level", "Thread" },
                        TimeFormat = "HH:mm:ss.fff",
                        Transformations = new[] { @"lic\t\[.*\]" },
                        XorMask = "0xef"
                    }
                }, indent));
            file.Save();

            var configuration = new ConfigurationBuilder()
                .AddYamlFile(_tempFile, optional: false, reloadOnChange: false)
                .Build();

            Assert.AreEqual("error: 0x0", configuration["Settings:ColorSettings:Rules:0:RegexString"]);
            Assert.AreEqual(regex, configuration["Settings:LogFormats:0:Regex"]);
            Assert.AreEqual("Thread", configuration["Settings:LogFormats:0:IndexedFields:1"]);
            Assert.AreEqual("HH:mm:ss.fff", configuration["Settings:LogFormats:0:TimeFormat"]);
            Assert.AreEqual(@"lic\t\[.*\]", configuration["Settings:LogFormats:0:Transformations:0"]);
            Assert.AreEqual("0xef", configuration["Settings:LogFormats:0:XorMask"]);
            Assert.AreEqual("prune", configuration["Settings:ViewSettings:BigLine"]);
        }

        [TestMethod]
        public void ReplaceSequenceInsertsBlockWhenKeyHasNoItems()
        {
            WriteFile(
                "Settings:\n" +
                "  ColorSettings:\n" +
                "    Rules:\n" +
                "  ViewSettings:\n" +
                "    BigLine: prune\n");

            var file = new YamlSettingsFile(_tempFile);
            file.ReplaceSequence("ColorSettings", "Rules", indent => SettingsYamlRenderer.RenderColorRules(
                new List<ColorRuleData>
                {
                    new() { RegexString = "IMP", ForegroundColor = "Red" }
                }, indent));
            file.Save();

            var text = File.ReadAllText(_tempFile);
            StringAssert.Contains(text, "    Rules:");
            StringAssert.Contains(text, "      - RegexString: IMP");
            StringAssert.Contains(text, "  ViewSettings:");
        }

        [TestMethod]
        public void ReplaceSequenceRoundTripsTrickyScalars()
        {
            WriteFile(
                "Settings:\n" +
                "  ViewSettings:\n" +
                "    BigLine: prune\n" +
                "  ColorSettings:\n" +
                "    Rules:\n" +
                "  LogFormats:\n");

            const string transformation = @"lic\t\[.*ContentImp.*\].*""TicketBody""\s+:\s+\{[^}]+""Data""\s+:\s+(?'Base64Decode'""[^""]+"")";
            const string quoted = "a: 'b'";
            const string regex = @"^(?'Time'\d{2}\:\d{2}\:\d{2}\.\d{3})\t(?'Thread'0x[0-9a-fA-F]+)\t(?'Severity'\w+)\t(?:(?'Component'[^\t]+)?\t)?(?'Message'.*)";

            var file = new YamlSettingsFile(_tempFile);
            file.ReplaceSequence("ColorSettings", "Rules", indent => SettingsYamlRenderer.RenderColorRules(
                new List<ColorRuleData>
                {
                    new() { RegexString = quoted }
                }, indent));
            file.ReplaceSequence("Settings", "LogFormats", indent => SettingsYamlRenderer.RenderLogFormats(
                new List<LogFormatData>
                {
                    new()
                    {
                        Regex = regex,
                        IndexedFields = new[] { "Thread", "Severity" },
                        Transformations = new[] { transformation }
                    }
                }, indent));
            file.Save();

            var configuration = new ConfigurationBuilder()
                .AddYamlFile(_tempFile, optional: false, reloadOnChange: false)
                .Build();

            Assert.AreEqual(quoted, configuration["Settings:ColorSettings:Rules:0:RegexString"]);
            Assert.AreEqual(regex, configuration["Settings:LogFormats:0:Regex"]);
            Assert.AreEqual(transformation, configuration["Settings:LogFormats:0:Transformations:0"]);
        }

        [TestMethod]
        public void ReplaceSequenceUnchangedLogFormatsKeepsComments()
        {
            const string regex = @"^(?'Time'\d{2}\:\d{2}\:\d{2}\.\d{3})\t(?'Thread'0x[0-9a-fA-F]+)\t(?'Severity'\w+)\t(?:(?'Component'[^\t]+)?\t)?(?'Message'.*)";
            const string transformation = @"lic\t\[.*ContentImp.*\].*""TicketBody""\s+:\s+\{[^}]+""Data""\s+:\s+(?'Base64Decode'""[^""]+"")";

            WriteFile(
                "Settings:\n" +
                "  LogFormats:\n" +
                "    # Tab-separated logs\n" +
                "    - Regex: " + regex + "\n" +
                "      IndexedFields:\n" +
                "        - Thread\n" +
                "        - Severity\n" +
                "        - Component\n" +
                "      TimeFormat: HH:mm:ss.fff\n" +
                "      Transformations:\n" +
                "        - " + transformation + "\n" +
                "    # xor-ed tab-separated logs\n" +
                "    - Regex: " + regex + "\n" +
                "      TimeFormat: HH:mm:ss.fff\n" +
                "      XorMask : 0xef\n");

            var file = new YamlSettingsFile(_tempFile);
            file.ReplaceSequence("Settings", "LogFormats", indent => SettingsYamlRenderer.RenderLogFormats(
                new List<LogFormatData>
                {
                    new()
                    {
                        Regex = regex,
                        IndexedFields = new[] { "Thread", "Severity", "Component" },
                        TimeFormat = "HH:mm:ss.fff",
                        Transformations = new[] { transformation }
                    },
                    new()
                    {
                        Regex = regex,
                        TimeFormat = "HH:mm:ss.fff",
                        XorMask = "0xef"
                    }
                }, indent));
            file.Save();

            Assert.IsFalse(file.HasChanges);
            var text = File.ReadAllText(_tempFile);
            StringAssert.Contains(text, "# Tab-separated logs");
            StringAssert.Contains(text, "# xor-ed tab-separated logs");
        }

        private void WriteFile(string content)
        {
            File.WriteAllText(_tempFile, content);
        }
    }
}