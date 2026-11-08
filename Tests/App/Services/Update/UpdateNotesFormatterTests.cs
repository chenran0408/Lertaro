using Lertaro.App.Services.Update;

namespace Lertaro.App.Tests.Services.Update;

[TestClass]
public sealed class UpdateNotesFormatterTests
{
    [TestMethod]
    public void Format_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.AreEqual(0, UpdateNotesFormatter.Format(null).Count);
        Assert.AreEqual(0, UpdateNotesFormatter.Format("   \n  ").Count);
    }

    [TestMethod]
    public void Format_HeadersAndBullets_AreRecognized()
    {
        var lines = UpdateNotesFormatter.Format("## 搜索更聪明\n\n- 四个过滤关键词\n- 频率上浮");
        Assert.AreEqual(3, lines.Count);
        Assert.AreEqual(("搜索更聪明", true), (lines[0].Text, lines[0].IsHeader));
        Assert.AreEqual(("\u2022 四个过滤关键词", false), (lines[1].Text, lines[1].IsHeader));
        Assert.AreEqual(("\u2022 频率上浮", false), (lines[2].Text, lines[2].IsHeader));
    }

    [TestMethod]
    public void Format_StripstDetailsBlockAndRules()
    {
        const string body = "# Lertaro 6.7.2\n\n正文。\n\n<details><summary>文件校验信息（可选）</summary>\n\nSHA-256：\n- **a.exe**: `abc`\n</details>\n\n---\n";
        var lines = UpdateNotesFormatter.Format(body);
        // The details block, the SHA lines inside it, and the trailing rule never render.
        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual(("Lertaro 6.7.2", true), (lines[0].Text, lines[0].IsHeader));
        Assert.AreEqual(("正文。", false), (lines[1].Text, lines[1].IsHeader));
    }

    [TestMethod]
    public void Format_StripsBoldCodeAndLinks()
    {
        var lines = UpdateNotesFormatter.Format("- **加粗** 和 `代码` 与 [链接](https://example.com)");
        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual("\u2022 加粗 和 代码 与 链接", lines[0].Text);
    }

    [TestMethod]
    public void Format_PlainText_PassesThrough()
    {
        var lines = UpdateNotesFormatter.Format("just a line");
        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual(("just a line", false), (lines[0].Text, lines[0].IsHeader));
    }
}
