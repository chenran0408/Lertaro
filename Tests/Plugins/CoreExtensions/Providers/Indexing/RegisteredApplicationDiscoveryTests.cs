using Lertaro.Plugins.CoreExtensions.Providers.Indexing;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Indexing;

[TestClass]
public sealed class RegisteredApplicationDiscoveryTests
{
    [TestMethod]
    [DataRow(@"C:\Apps\App.exe", @"C:\Apps\App.exe")]
    [DataRow("\"C:\\Apps with spaces\\App.exe\"", @"C:\Apps with spaces\App.exe")]
    [DataRow(@"C:\Apps\App", @"C:\Apps\App.exe")]
    public void FullyQualifiedRegisteredExecutablesAreAccepted(string value, string expected) =>
        Assert.AreEqual(expected, RegisteredApplicationDiscovery.NormalizePath(value));

    [TestMethod]
    [DataRow("App.exe")]
    [DataRow(@"\\server\share\App.exe")]
    [DataRow("\"C:\\Apps\\App.exe\" --safe")]
    [DataRow(@"C:\Apps\readme.txt")]
    [DataRow(" ")]
    public void InvalidAndRemoteRegistrationValuesAreNotTreatedAsLocalPrograms(string value) =>
        Assert.IsNull(RegisteredApplicationDiscovery.NormalizePath(value));

    [TestMethod]
    public void RegistryViewsMergeEquivalentTargetsButRetainDifferentInstallations()
    {
        var entries = new[] { new RegisteredApplication("editor.exe", @"C:\A\Editor.exe"), new RegisteredApplication("edit.exe", @"c:\a\editor.exe"), new RegisteredApplication("editor.exe", @"D:\B\Editor.exe"), new RegisteredApplication("gone.exe", @"C:\Gone.exe") };
        var results = RegisteredApplicationDiscovery.Collect(entries, path => !path.Contains("Gone"), _ => "Editor");
        Assert.HasCount(2, results);
        Assert.HasCount(2, results[0].Keywords);
        Assert.IsTrue(results.All(result => result.Title == "Editor" && result.ResultKind == "Application"));
    }

    [TestMethod]
    public void MissingFriendlyTitleFallsBackToExecutableName() =>
        Assert.AreEqual("Editor", RegisteredApplicationDiscovery.Collect([new("editor.exe", @"C:\Apps\Editor.exe")], _ => true, _ => null)[0].Title);

    [TestMethod]
    public void RegistrationFingerprintIsStableAcrossEnumerationOrderAndCase()
    {
        var first = RegisteredApplicationDiscovery.Fingerprint([new("Editor.exe", @"C:\Apps\Editor.exe"), new("Paint.exe", @"C:\Apps\Paint.exe")]);
        var second = RegisteredApplicationDiscovery.Fingerprint([new("PAINT.EXE", @"c:\apps\paint.exe"), new("EDITOR.EXE", @"c:\apps\editor.exe"), new("Editor.exe", @"C:\Apps\Editor.exe")]);
        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, RegisteredApplicationDiscovery.Fingerprint([new("Editor.exe", @"D:\Apps\Editor.exe")]));
    }
}
