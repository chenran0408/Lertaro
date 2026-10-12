using Lertaro.App.Services.Plugin;
namespace Lertaro.App.Tests.Services.Plugin;
[TestClass]
public sealed class ApplicationLaunchDispatcherTests
{
    [TestMethod]
    [DataRow(false, "")]
    [DataRow(true, "runas")]
    public void ParameterizedLaunchKeepsItsOptionsIncludingAdministrativeLaunch(bool admin, string verb)
    {
        var start = ApplicationLaunchDispatcher.BuildParameterizedStartInfo(new() { FullPath = @"D:\Apps\Editor.exe", ApplicationArguments = "--profile Work", ApplicationWorkingDirectory = @"D:\Projects" }, admin);
        Assert.IsNotNull(start);
        Assert.AreEqual("--profile Work", start.Arguments);
        Assert.AreEqual(@"D:\Projects", start.WorkingDirectory);
        Assert.AreEqual(verb, start.Verb);
    }
}
