using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lertaro.Plugins.TaskbarAppearance.Tests;

[TestClass]
public sealed class TaskbarConfigurationTests
{
    [TestMethod]
    public void ColorUsesRgbaAndPreservesUnrelatedSettingsAndWindowRules()
    {
        var json = TaskbarConfiguration.Build("""
            { // External configuration may contain comments and trailing commas.
              "hide_tray": true,
              "ignored_windows": { "process_name": ["game.exe"] },
              "maximized_window_appearance": {"enabled":true,"rules":{"process_name":{"game.exe":{"accent":"opaque"}}}},
            }
            """, new TaskbarOptions { Enabled = true, Color = "#aabbcc", Opacity = 50 });
        var root = JsonNode.Parse(json)!;
        Assert.AreEqual("#AABBCC80", root["desktop_appearance"]!["color"]!.GetValue<string>());
        Assert.AreEqual("clear", root["desktop_appearance"]!["accent"]!.GetValue<string>());
        Assert.IsTrue(root["hide_tray"]!.GetValue<bool>());
        Assert.AreEqual("game.exe", root["ignored_windows"]!["process_name"]![0]!.GetValue<string>());
        Assert.IsNotNull(root["maximized_window_appearance"]!["rules"]);
        Assert.IsFalse(root["maximized_window_appearance"]!["enabled"]!.GetValue<bool>());
        Assert.AreEqual("zh-CN", root["language"]!.GetValue<string>());
    }

    [TestMethod]
    public void EveryDynamicSituationCanHaveAnIndependentStyle()
    {
        var options = new TaskbarOptions { Enabled = true, Rules = TaskbarConfiguration.Situations.Select(situation =>
            new TaskbarRule { Situation = situation, Style = "opaque", Color = "#123456", Opacity = 100 }).ToList() };
        var root = JsonNode.Parse(TaskbarConfiguration.Build("{}", options))!;
        foreach (var situation in TaskbarConfiguration.Situations)
        {
            Assert.IsTrue(root[situation]!["enabled"]!.GetValue<bool>());
            Assert.AreEqual("#123456FF", root[situation]!["color"]!.GetValue<string>());
        }
    }

    [TestMethod]
    public void DisablingRestoresNormalAppearanceAndTurnsOffAllOverrides()
    {
        var root = JsonNode.Parse(TaskbarConfiguration.Build("{}", new TaskbarOptions()))!;
        Assert.AreEqual("normal", root["desktop_appearance"]!["accent"]!.GetValue<string>());
        Assert.IsTrue(root["desktop_appearance"]!["show_line"]!.GetValue<bool>());
        Assert.IsTrue(root["desktop_appearance"]!["show_peek"]!.GetValue<bool>());
        foreach (var situation in TaskbarConfiguration.Situations)
            Assert.IsFalse(root[situation]!["enabled"]!.GetValue<bool>());
    }

    [TestMethod]
    [DataRow("#12345", 50)]
    [DataRow("red", 50)]
    [DataRow("#123456", -1)]
    [DataRow("#123456", 101)]
    public void InvalidColorsAndOpacityAreRejected(string color, int opacity)
        => Assert.ThrowsExactly<ArgumentException>(() => TaskbarConfiguration.Build("{}", new TaskbarOptions { Enabled = true, Color = color, Opacity = opacity }));

    [TestMethod]
    public void DuplicateOrUnknownSituationsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => TaskbarConfiguration.Build("{}", new TaskbarOptions
        { Enabled = true, Rules = [new(), new()] }));
        Assert.ThrowsExactly<ArgumentException>(() => TaskbarConfiguration.Build("{}", new TaskbarOptions
        { Enabled = true, Rules = [new() { Situation = "unknown" }] }));
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("{")]
    public void InvalidExistingConfigurationIsRejected(string json)
        => Assert.Throws<JsonException>(() => TaskbarConfiguration.Build(json, new TaskbarOptions { Enabled = true }));

    [TestMethod]
    public void LanguageChoiceCanLeaveExistingLanguageAlone()
    {
        var root = JsonNode.Parse(TaskbarConfiguration.Build("{\"language\":\"ja-JP\"}", new TaskbarOptions { Enabled = true, UseChinese = false }))!;
        Assert.AreEqual("ja-JP", root["language"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow(@"C:\Program Files\WindowsApps\28017CharlesMilette.TranslucentTB_2026_x64\TranslucentTB.exe", null, true)]
    [DataRow(@"D:\Tools\TranslucentTB.exe", null, false)]
    [DataRow(@"D:\Tools\TranslucentTB.exe", @"D:\Tools", true)]
    [DataRow(@"D:\Other\TranslucentTB.exe", @"D:\Tools", false)]
    public void EngineSelectionDoesNotReuseADifferentCopy(string runningPath, string? portableDirectory, bool expected)
        => Assert.AreEqual(expected, TaskbarEngine.MatchesEngine(runningPath, portableDirectory));
}
