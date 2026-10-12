using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.Views.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
namespace Lertaro.App.Tests.Views.Settings.Plugins;
[TestClass]
[DoNotParallelize]
public sealed class PluginConfigFieldRowTemplateTests
{
    [StaTestMethod]
    public void ProgramAndFolderRowsRenderAndReflectAStagedNameChange()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("fixture", "programs", new List<Dictionary<string, object>> { new Dictionary<string, object> { ["Name"] = "", ["Path"] = @"D:\Portable\Editor.exe" } });
        var field = new PluginConfigFieldViewModel("fixture", new PluginConfigField
        {
            Key = "programs", LabelKey = "Programs", FieldType = ConfigFieldType.Array, DefaultValue = new List<object>(), SubFields =
            [new() { Key = "Name", FieldType = ConfigFieldType.Text, DefaultValue = "" }, new() { Key = "Path", FieldType = ConfigFieldType.FilePath, DefaultValue = "" }]
        }, settings, null);
        var content = new ContentControl { Content = field };
        var window = new Window { Width = 900, Height = 700, Content = content };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles.xaml") });
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Lertaro.App;component/Views/Settings/Plugins/FieldRowTemplate.xaml") });
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(Texts(content).Any(text => text.Text == "Editor.exe"), $"Rows={field.ArrayItems.Count}; visible texts={string.Join(" | ", Texts(content).Select(text => text.Text))}");
            field.ArrayItems[0].Children[0].Value = "Work editor";
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(Texts(content).Any(text => text.Text == "Work editor"));
            Assert.AreEqual("", settings.GetPluginSetting<List<Dictionary<string, object>>>("fixture", "programs", [])[0]["Name"]);
        }
        finally { window.Close(); }
    }
    private static IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Texts(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
