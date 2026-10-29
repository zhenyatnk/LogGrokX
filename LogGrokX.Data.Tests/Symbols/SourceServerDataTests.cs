using LogGrokX.Data.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests.Symbols;

[TestClass]
public class SourceServerDataTests
{
    [TestMethod]
    public void ExpandsHttpTarget()
    {
        var data = SourceServerData.Parse(
            "SRCSRV: ini ------------------------------------------------\r\n" +
            "VERSION=2\r\n" +
            "SRCSRV: variables ------------------------------------------\r\n" +
            "SRCSRVVERCTRL=https\r\n" +
            "SRCSRVTRG=https://raw.example.com/repo/%var2%/%var3%\r\n" +
            "SRCSRV: source files ---------------------------------------\r\n" +
            @"C:\build\src\main.cpp*0123abcd*src/main.cpp" + "\r\n" +
            "SRCSRV: end ------------------------------------------------\r\n");

        Assert.AreEqual(1, data.FileCount);
        Assert.IsTrue(data.TryGetTarget(@"c:\build\src\main.cpp", @"C:\cache\src", out var target));
        Assert.AreEqual("https://raw.example.com/repo/0123abcd/src/main.cpp", target.Target);
        Assert.IsNull(target.Command);
    }

    [TestMethod]
    public void ExpandsTfsStyleCommand()
    {
        var data = SourceServerData.Parse(
            "SRCSRV: ini ------------------------------------------------\n" +
            "VERSION=3\n" +
            "SRCSRV: variables ------------------------------------------\n" +
            "TFS_EXTRACT_CMD=tf.exe view /version:%var4% /noprompt \"$%var3%\" /server:%fnvar%(%var2%) /output:%srcsrvtrg%\n" +
            "TFS_EXTRACT_TARGET=%targ%\\%var2%%fnbksl%(%var3%)\\%var4%\\%fnfile%(%var5%)\n" +
            "SRCSRVTRG=%TFS_extract_target%\n" +
            "SRCSRVCMD=%TFS_extract_cmd%\n" +
            "TFS=http://tfs.example.com:8080/tfs\n" +
            "SRCSRV: source files ---------------------------------------\n" +
            "D:\\a\\1\\s\\Lib\\File.cpp*TFS*/Project/Lib/File.cpp*1234*D:\\a\\1\\s\\Lib\\File.cpp\n" +
            "SRCSRV: end ------------------------------------------------\n");

        Assert.IsTrue(data.TryGetTarget(@"D:\a\1\s\Lib\File.cpp", @"C:\cache", out var target));
        Assert.AreEqual(@"C:\cache\TFS\Project\Lib\File.cpp\1234\File.cpp", target.Target);
        Assert.AreEqual(
            "tf.exe view /version:1234 /noprompt \"$/Project/Lib/File.cpp\" /server:http://tfs.example.com:8080/tfs /output:" + target.Target,
            target.Command);
    }

    [TestMethod]
    public void UnknownFileHasNoTarget()
    {
        var data = SourceServerData.Parse(
            "SRCSRV: variables ------------------------------------------\n" +
            "SRCSRVTRG=%var2%\n" +
            "SRCSRV: source files ---------------------------------------\n" +
            "a.cpp*b\n");

        Assert.IsFalse(data.TryGetTarget("c.cpp", "", out _));
        Assert.IsTrue(data.TryGetTarget("a.cpp", "", out var target));
        Assert.AreEqual("b", target.Target);
    }

    [TestMethod]
    public void RecursiveVariablesDoNotOverflow()
    {
        var data = SourceServerData.Parse(
            "SRCSRV: variables ------------------------------------------\n" +
            "A=%B%\n" +
            "B=%A%\n" +
            "SRCSRVTRG=%A%\n" +
            "SRCSRV: source files ---------------------------------------\n" +
            "a.cpp*b\n");

        Assert.IsTrue(data.TryGetTarget("a.cpp", "", out _));
    }
}
