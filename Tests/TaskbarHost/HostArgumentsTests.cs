namespace Lertaro.TaskbarHost.Tests;

[TestClass]
public sealed class HostArgumentsTests
{
    [TestMethod]
    public void AcceptsAbsolutePathsWithSpacesAndUnicode()
    {
        var result = HostArguments.Parse(["42", "123456789", @"D:\Taskbar data\背景\TranslucentTB.exe", @"D:\Taskbar data\ready.status"]);
        Assert.AreEqual(42, result.ParentId);
        Assert.AreEqual(123456789L, result.ParentStarted);
    }

    [TestMethod]
    [DataRow("-1", "123", @"D:\TranslucentTB.exe", @"D:\status")]
    [DataRow("1", "0", @"D:\TranslucentTB.exe", @"D:\status")]
    [DataRow("1", "123", "TranslucentTB.exe", @"D:\status")]
    [DataRow("1", "123", @"D:\other.exe", @"D:\status")]
    [DataRow("1", "123", @"D:\TranslucentTB.exe", "status")]
    public void RejectsMalformedArguments(string parent, string started, string engine, string status)
        => Assert.Throws<ArgumentException>(() => HostArguments.Parse([parent, started, engine, status]));
}
