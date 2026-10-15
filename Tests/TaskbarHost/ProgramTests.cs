using System.Diagnostics;
using System.Reflection;

namespace Lertaro.TaskbarHost.Tests;

[TestClass, DoNotParallelize]
public sealed class ProgramTests
{
    private static Task<int> Run(string[] args) => (Task<int>)typeof(HostArguments).Assembly
        .GetType("Lertaro.TaskbarHost.Program")!.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, [args])!;

    [TestMethod]
    public async Task MissingArgumentsDoNotStartAnEngine() => Assert.AreEqual(2, await Run([]));

    [TestMethod]
    public async Task ParentIdentityMismatchDoesNotTouchTheStatusFile()
    {
        using var parent = Process.GetCurrentProcess();
        var status = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".status");
        Assert.AreEqual(3, await Run([parent.Id.ToString(), (parent.StartTime.ToUniversalTime().Ticks + 1).ToString(), @"D:\missing\TranslucentTB.exe", status]));
        Assert.IsFalse(File.Exists(status));
    }

    [TestMethod]
    public async Task StartupFailureIsReportedWithoutEscapingToTheParent()
    {
        using var parent = Process.GetCurrentProcess();
        var status = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".status");
        try
        {
            var code = await Run([parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "TranslucentTB.exe"), status]);
            Assert.AreEqual(1, code);
            Assert.StartsWith("error:", await File.ReadAllTextAsync(status));
        }
        finally { File.Delete(status); }
    }
}
