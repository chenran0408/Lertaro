namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardDatabaseTests
{
    [TestMethod]
    public void QuarantinesDatabaseAndEveryRemainingSidecarWithTheSameSuffix()
    {
        var folder = Path.Combine(Path.GetTempPath(), "clipboard-quarantine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "history.db");
        string[] suffixes = ["", "-journal", "-wal", "-shm"];
        try
        {
            foreach (var suffix in suffixes) File.WriteAllText(path + suffix, "fixture" + suffix);
            new ClipboardDatabase(path).MoveCorruptDatabaseAside();
            var files = Directory.GetFiles(folder);
            Assert.HasCount(4, files);
            foreach (var suffix in suffixes)
            {
                Assert.IsFalse(File.Exists(path + suffix));
                var backup = Directory.GetFiles(folder, "history.db" + suffix + ".corrupt-*").Single();
                Assert.AreEqual("fixture" + suffix, File.ReadAllText(backup));
            }
            Assert.HasCount(1, files.Select(file => file[file.IndexOf(".corrupt-", StringComparison.Ordinal)..]).Distinct().ToArray());
        }
        finally
        {
            foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
