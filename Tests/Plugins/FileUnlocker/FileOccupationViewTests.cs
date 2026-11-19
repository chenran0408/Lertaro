namespace Lertaro.Plugins.FileUnlocker.Tests;

[TestClass]
public sealed class FileOccupationViewTests
{
    [STATestMethod]
    public void TargetPathStaysSelectableAndReadOnly()
    {
        const string path = @"D:\Files\A deeply nested example directory\locked.txt";
        var view = new FileOccupationView(path);
        var pathBox = Assert.IsInstanceOfType<System.Windows.Controls.TextBox>(view.FindName("PathText"));

        pathBox.SelectAll();
        Assert.IsTrue(pathBox.IsReadOnly);
        Assert.IsTrue(pathBox.Focusable);
        Assert.AreEqual(path, pathBox.SelectedText);
    }
}
