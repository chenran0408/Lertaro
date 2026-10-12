using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Lertaro.App.Services.ShellIcons;
using Lertaro.App.ViewModels.Search.Mapping;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
[DoNotParallelize]
public sealed class LaunchShortcutCacheTests
{
    [StaTestMethod]
    public void RealShortcutPreservesTargetArgumentsAndWorkingDirectoryWithoutOpeningTheTarget()
    {
        var root = Directory.CreateTempSubdirectory("LaunchShortcutTests-").FullName;
        var shortcut = Path.Combine(root, "Editor.lnk");
        var target = Path.Combine(root, "Editor.exe"); // Target need not exist or be launched to read a link.
        object? instance = null;
        try
        {
            instance = new ShellIconNativeMethods.ShellLink();
            var link = (ShellIconNativeMethods.IShellLinkW)instance;
            link.SetPath(target);
            link.SetArguments("--profile TestProfile");
            link.SetWorkingDirectory(root);
            ((IPersistFile)instance).Save(shortcut, true);
            var info = LaunchShortcutCache.Read(shortcut);
            Assert.IsNotNull(info);
            Assert.AreEqual(target, info.Value.Target);
            Assert.AreEqual("--profile TestProfile", info.Value.Arguments);
            Assert.AreEqual(root, info.Value.WorkingDirectory);
            Assert.AreEqual(info, LaunchShortcutCache.Read(shortcut));
            Assert.IsFalse(File.Exists(target));
        }
        finally
        {
            if (instance != null) Marshal.FinalReleaseComObject(instance);
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void RemoteAndRelativeShortcutsAreNotOpenedForRanking()
    {
        Assert.IsNull(LaunchShortcutCache.Read(@"\\remote-server\share\Editor.lnk"));
        Assert.IsNull(LaunchShortcutCache.Read("Editor.lnk"));
    }
}
