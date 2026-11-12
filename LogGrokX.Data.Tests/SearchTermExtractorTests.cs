using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class SearchTermExtractorTests
{
    [TestMethod]
    [DataRow("error in module", "error", "error")]
    [DataRow("error in module", "module", "module")]
    [DataRow("Event = 1244", "Event", "Event = 1244")]
    [DataRow("Event = 1244", "1244", "1244")]
    [DataRow("Event = 1244", "=", "Event = 1244")]
    [DataRow("Event=1244", "Event", "Event=1244")]
    [DataRow("Event=1244", "1244", "1244")]
    [DataRow("Event   =    1244 next", "1244", "1244")]
    [DataRow("Value: tests", "Value", "Value: tests")]
    [DataRow("Value: tests", "tests", "tests")]
    [DataRow("Value:tests", "tests", "tests")]
    [DataRow("Value :\ttests", "Value", "Value :\ttests")]
    [DataRow("a=1, b=2", "b", "b=2")]
    [DataRow("a=1, b=2", "1", "1")]
    [DataRow("user.name = john.doe.", "doe", "john.doe")]
    [DataRow("retry = -5", "5", "-5")]
    [DataRow("open http://host/path", "http", "http")]
    [DataRow("Value: (tests)", "tests", "tests")]
    [DataRow("[alpha::beta::gamma::delta::GetTargetPath] Processing item",
        "[", "alpha::beta::gamma::delta::GetTargetPath")]
    [DataRow("[alpha::beta::gamma::delta::GetTargetPath] Processing item",
        "]", "alpha::beta::gamma::delta::GetTargetPath")]
    [DataRow("<alpha::beta>", "<", "alpha::beta")]
    [DataRow("(alpha beta)", "(", "alpha beta")]
    [DataRow("[outer [inner] tail]", "[", "outer [inner] tail")]
    [DataRow("artifact = 'payload_42.bin'", "artifact", "artifact = 'payload_42.bin'")]
    [DataRow("artifact = 'payload_42.bin'", "payload_42", "'payload_42.bin'")]
    [DataRow("artifact = 'payload_42.bin'", "=", "artifact = 'payload_42.bin'")]
    [DataRow("name = \"file name.dat\"", "file name", "\"file name.dat\"")]
    [DataRow(@"volumePath: \\?\Volume{12345678-abcd-4321-9876-0123456789ab}\", "volumePath",
        @"volumePath: \\?\Volume{12345678-abcd-4321-9876-0123456789ab}\")]
    [DataRow(@"volumePath: \\?\Volume{12345678-abcd-4321-9876-0123456789ab}\", "Volume",
        @"\\?\Volume{12345678-abcd-4321-9876-0123456789ab}\")]
    [DataRow(@"volumePath: \\?\Volume{12345678-abcd-4321-9876-0123456789ab}\", "12345678",
        @"\\?\Volume{12345678-abcd-4321-9876-0123456789ab}\")]
    [DataRow(@"deviceName: \Device\SyntheticVolume7", "deviceName", @"deviceName: \Device\SyntheticVolume7")]
    [DataRow(@"deviceName: \Device\SyntheticVolume7", ":", @"deviceName: \Device\SyntheticVolume7")]
    [DataRow(@"deviceName: \Device\SyntheticVolume7", "Device", @"\Device\SyntheticVolume7")]
    [DataRow(@"deviceName: \Device\SyntheticVolume7", "SyntheticVolume7", @"\Device\SyntheticVolume7")]
    [DataRow("Building provider factory='acme_demo.inventory.SampleProvider', providerKey=0x1234abcd",
        "Building", "Building")]
    [DataRow("Building provider factory='acme_demo.inventory.SampleProvider', providerKey=0x1234abcd",
        "factory", "factory='acme_demo.inventory.SampleProvider'")]
    [DataRow("Building provider factory='acme_demo.inventory.SampleProvider', providerKey=0x1234abcd",
        "SampleProvider", "'acme_demo.inventory.SampleProvider'")]
    [DataRow("Building provider factory='acme_demo.inventory.SampleProvider', providerKey=0x1234abcd",
        "providerKey", "providerKey=0x1234abcd")]
    [DataRow("Building provider factory='acme_demo.inventory.SampleProvider', providerKey=0x1234abcd",
        "0x1234abcd", "0x1234abcd")]
    [DataRow("10:11:12.013\t0x7f\tDBG\tdemo\tCan't dispatch request iface=0x1234abcd, providerKey=0x00000000, hostId=0x00000000, accessPointId=0x00000000, caller={placeholder}. 0xdeadbeef (Can't locate provider)",
        "Can't", "Can't")]
    [DataRow("name='Can't'", "Can't", "'Can't'")]
    [DataRow("caller={placeholder}", "caller", "caller={placeholder}")]
    [DataRow("caller={placeholder}", "=", "caller={placeholder}")]
    [DataRow("caller={placeholder}", "{", "{placeholder}")]
    [DataRow("caller={placeholder}", "placeholder", "{placeholder}")]
    [DataRow("caller={placeholder}", "}", "{placeholder}")]
    public void ExtractsTermUnderPosition(string text, string clicked, string expected)
    {
        var position = text.IndexOf(clicked, System.StringComparison.Ordinal) + clicked.Length / 2;

        Assert.AreEqual(expected, SearchTermExtractor.GetTerm(text, position));
    }

    [TestMethod]
    [DataRow("a  b", 2)]
    [DataRow("x = ", 2)]
    [DataRow("", 0)]
    public void ReturnsNullWithoutTerm(string text, int position)
    {
        Assert.IsNull(SearchTermExtractor.GetTerm(text, position));
    }

    [TestMethod]
    public void AppendsTermsWithWildcard()
    {
        var pattern = SearchTermExtractor.AppendToPattern(string.Empty, false, "Event = 1244");
        Assert.AreEqual("Event = 1244", pattern);

        pattern = SearchTermExtractor.AppendToPattern(pattern, true, "Value: a.b");
        Assert.AreEqual(@"Event = 1244.*Value: a\.b", pattern);
        Assert.IsTrue(Regex.IsMatch("x Event = 1244 y Value: a.b", pattern));
        Assert.IsFalse(Regex.IsMatch("Value: a.b Event = 1244", pattern));
    }

    [TestMethod]
    public void EscapesPlainTextPatternBeforeAppending()
    {
        Assert.AreEqual(@"a\+b.*c", SearchTermExtractor.AppendToPattern("a+b", false, "c"));
    }
}
