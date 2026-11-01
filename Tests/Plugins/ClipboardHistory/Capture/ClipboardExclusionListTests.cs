using Lertaro.Plugins.ClipboardHistory.Capture;

namespace Lertaro.Plugins.ClipboardHistory.Tests.Capture;

[TestClass]
public sealed class ClipboardExclusionListTests
{
    [TestMethod]
    public void IsExcluded_MatchesCaseInsensitive()
    {
        Assert.IsTrue(ClipboardExclusionList.IsExcluded("KeePass,bitwarden", "keepass"));
        Assert.IsTrue(ClipboardExclusionList.IsExcluded("keepass,bitwarden", "Bitwarden"));
    }

    [TestMethod]
    public void IsExcluded_NormalizesExeSuffixInSetting() => Assert.IsTrue(ClipboardExclusionList.IsExcluded("KeePass.exe", "keepass"));

    [TestMethod]
    public void IsExcluded_NormalizesExeSuffixInProcessName() => Assert.IsTrue(ClipboardExclusionList.IsExcluded("keepass", "keepass.exe"));

    [TestMethod]
    public void IsExcluded_EmptyListAllowsEverything()
    {
        Assert.IsFalse(ClipboardExclusionList.IsExcluded("", "keepass"));
        Assert.IsFalse(ClipboardExclusionList.IsExcluded(null, "keepass"));
        Assert.IsFalse(ClipboardExclusionList.IsExcluded("   ", "keepass"));
    }

    [TestMethod]
    public void IsExcluded_IgnoresWhitespaceAndEmptyEntries()
    {
        Assert.IsTrue(ClipboardExclusionList.IsExcluded(" , keepass , ,", "keepass"));
        Assert.IsFalse(ClipboardExclusionList.IsExcluded(" , , ,", "keepass"));
    }

    [TestMethod]
    public void IsExcluded_DoesNotMatchSubstring()
    {
        Assert.IsFalse(ClipboardExclusionList.IsExcluded("keepas", "keepass"));
        Assert.IsFalse(ClipboardExclusionList.IsExcluded("keepass", "keepassxc"));
    }

    [TestMethod]
    public void IsExcluded_UnknownProcessNameFailsClosed()
    {
        Assert.IsTrue(ClipboardExclusionList.IsExcluded("", null));
        Assert.IsTrue(ClipboardExclusionList.IsExcluded("", ""));
        Assert.IsTrue(ClipboardExclusionList.IsExcluded("", "   "));
    }

    [TestMethod]
    public void Parse_ReturnsNormalizedEntries()
    {
        var entries = ClipboardExclusionList.Parse("KeePass.exe, bitwarden ,, 1Password");
        Assert.HasCount(3, entries);
        Assert.IsTrue(entries.Contains("keepass"));
        Assert.IsTrue(entries.Contains("bitwarden"));
        Assert.IsTrue(entries.Contains("1password"));
    }

    [TestMethod]
    public void Parse_EmptyInputReturnsEmptySet()
    {
        Assert.HasCount(0, ClipboardExclusionList.Parse(null));
        Assert.HasCount(0, ClipboardExclusionList.Parse(""));
    }
}
