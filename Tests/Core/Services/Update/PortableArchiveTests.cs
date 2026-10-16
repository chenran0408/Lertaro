using System.Diagnostics;
using System.IO.Compression;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class PortableArchiveTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Archive_PreservesHiddenFilesNestedPluginsAndUnicode_WhenReplacingAnOlderZip(bool modernHost)
    {
        var host = modernHost
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(host)) Assert.Inconclusive("Requested build host is not installed.");
        var root = Directory.CreateTempSubdirectory("LertaroArchive_").FullName;
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "Lertaro")).FullName;
            var files = new Dictionary<string, byte[]>
            {
                ["Lertaro.App.exe"] = [0, 1, 2, 255],
                ["Plugins/嵌套插件/资源.json"] = System.Text.Encoding.UTF8.GetBytes("中文资源"),
                [".hidden"] = [17, 18],
                ["empty.txt"] = []
            };
            foreach (var (name, bytes) in files)
            {
                var path = Path.Combine(source, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            File.SetAttributes(Path.Combine(source, ".hidden"), FileAttributes.Hidden);
            Directory.CreateDirectory(Path.Combine(source, "Plugins", "empty"));
            var zip = Path.Combine(root, "Lertaro-Portable.zip");
            File.WriteAllText(zip, "old archive must be replaced");
            var start = new ProcessStartInfo(host)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File",
                Path.Combine(AppContext.BaseDirectory, "NewPortableArchive.ps1"), "-SourceDirectory", source, "-DestinationPath", zip })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, process.ExitCode, await error + await output);
            using var archive = ZipFile.OpenRead(zip);
            Assert.HasCount(files.Count + 1, archive.Entries);
            Assert.IsNotNull(archive.GetEntry("Lertaro/Plugins/empty/"));
            foreach (var (name, expected) in files)
            {
                var entry = archive.GetEntry("Lertaro/" + name);
                Assert.IsNotNull(entry, name);
                using var stream = entry.Open();
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                CollectionAssert.AreEqual(expected, bytes.ToArray(), name);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
