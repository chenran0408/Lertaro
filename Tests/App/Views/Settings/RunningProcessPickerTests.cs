using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.Views.Settings;

namespace Lertaro.App.Tests.Views.Settings;

[TestClass]
[DoNotParallelize]
public sealed class RunningProcessPickerTests
{
    [StaTestMethod]
    public void Picker_LaysOutExpandedWithNoBrokenBindings()
    {
        var errors = new BindingErrorListener();
        var previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            var vm = new ProcessBlacklistEditorViewModel([]);
            vm.SetRunningProcesses([new RunningProcessInfo("editor.exe", "Document", true)]);
            var picker = new RunningProcessPicker { DataContext = vm };
            picker.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles/Windows/SettingsWindow.xaml")
            });
            var expander = (Expander)picker.Content;
            // Expand only the view: this test uses a snapshot, not the machine's running processes.
            BindingOperations.ClearBinding(expander, Expander.IsExpandedProperty);
            expander.IsExpanded = true;
            picker.Measure(new Size(700, 600));
            picker.Arrange(new Rect(0, 0, 700, 600));
            picker.UpdateLayout();
            Assert.IsGreaterThan(0, picker.DesiredSize.Height);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
            PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
        }
        Assert.IsEmpty(errors.Messages, string.Join(Environment.NewLine, errors.Messages));
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Messages { get; } = new();
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message)) Messages.Add(message);
        }
    }
}
