using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Lertaro.App.Services.QuickPanel;
using Lertaro.App.Services.ShellIcons;

namespace Lertaro.App.Tests.Services.QuickPanel;

[TestClass]
public sealed class QuickPanelShortcutDropTests
{
    [TestMethod]
    [DataRow(@"C:\tools\app.exe", false, "app.lnk")]
    [DataRow(@"C:\data\folder.v2\", true, "folder.v2.lnk")]
    [DataRow(@"C:\", true, "C.lnk")]
    public void ShortcutNamesHandleFilesFoldersAndDriveRoots(string source, bool isDirectory, string expected)
        => Assert.AreEqual(expected, QuickPanelShortcutDrop.ShortcutName(source, isDirectory));

    [StaTestMethod]
    public void AddingFilesAndFoldersCreatesWorkingLinksWithoutChangingOriginals()
    {
        var root = Path.Combine(Path.GetTempPath(), "LertaroShortcutDropTests", Guid.NewGuid().ToString("N"));
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            var panel = Directory.CreateDirectory(Path.Combine(root, "panel")).FullName;
            var document = Path.Combine(source, "report.txt");
            File.WriteAllText(document, "original content");
            var folder = Directory.CreateDirectory(Path.Combine(source, "folder.v2")).FullName;
            File.WriteAllText(Path.Combine(panel, "report.lnk"), "existing item");
            QuickPanelShortcutDrop.Add(new[] { document, folder, document, Path.Combine(source, "missing.txt") }, panel);
            Assert.AreEqual("original content", File.ReadAllText(document));
            Assert.IsTrue(Directory.Exists(folder));
            Assert.AreEqual("existing item", File.ReadAllText(Path.Combine(panel, "report.lnk")));
            Assert.AreEqual(document, TargetOf(Path.Combine(panel, "report (2).lnk")));
            Assert.AreEqual(folder, TargetOf(Path.Combine(panel, "folder.v2.lnk")));
            Assert.HasCount(3, Directory.GetFiles(panel));
            var copied = Directory.CreateDirectory(Path.Combine(root, "copied")).FullName;
            var originalLink = Path.Combine(panel, "report (2).lnk");
            QuickPanelShortcutDrop.Add(new[] { originalLink }, copied);
            CollectionAssert.AreEqual(File.ReadAllBytes(originalLink), File.ReadAllBytes(Path.Combine(copied, "report (2).lnk")));
            QuickPanelShortcutDrop.Add(new[] { originalLink }, panel);
            Assert.HasCount(3, Directory.GetFiles(panel));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string TargetOf(string shortcut)
    {
        var linkObject = new ShellIconNativeMethods.ShellLink();
        try
        {
            ((IPersistFile)linkObject).Load(shortcut, 0);
            var path = new StringBuilder(1024);
            ((ShellIconNativeMethods.IShellLinkW)linkObject).GetPath(path, path.Capacity, IntPtr.Zero, 0);
            return path.ToString();
        }
        finally { Marshal.FinalReleaseComObject(linkObject); }
    }
}
