using Lertaro.Core.Extensions;

namespace Lertaro.Core.Tests;

[TestClass]
public sealed class PathFormatHelperTests
{
    private static readonly string[] Paths = [@"C:\Users\test\my file.txt", @"D:\data\report.pdf"];

    [TestMethod]
    public void ToQuoted_WrapsEachPathInDoubleQuotes()
    {
        CollectionAssert.AreEqual(
            new[] { "\"C:\\Users\\test\\my file.txt\"", "\"D:\\data\\report.pdf\"" },
            PathFormatHelper.ToQuoted(Paths).ToArray());
    }

    [TestMethod]
    public void ToForwardSlash_ReplacesBackslashes()
    {
        CollectionAssert.AreEqual(
            new[] { "C:/Users/test/my file.txt", "D:/data/report.pdf" },
            PathFormatHelper.ToForwardSlash(Paths).ToArray());
    }

    [TestMethod]
    public void ToUnc_ConvertsDrivePaths_SkipsAlreadyUnc()
    {
        var paths = new[] { @"C:\data\file.txt", @"\\server\share\file.txt" };
        var result = PathFormatHelper.ToUnc(paths).ToArray();
        Assert.AreEqual(@"\\localhost\c$\data\file.txt", result[0]);
        Assert.AreEqual(@"\\server\share\file.txt", result[1]); // already UNC: pass through
    }

    [TestMethod]
    public void ToFileName_ExtractsNameOnly()
    {
        CollectionAssert.AreEqual(
            new[] { "my file.txt", "report.pdf" },
            PathFormatHelper.ToFileName(Paths).ToArray());
    }

    [TestMethod]
    public void ToDirectory_ExtractsParentDirectory()
    {
        var result = PathFormatHelper.ToDirectory(Paths).ToArray();
        Assert.IsTrue(result[0].EndsWith("test"));
        Assert.IsTrue(result[1].EndsWith("data"));
    }
}
