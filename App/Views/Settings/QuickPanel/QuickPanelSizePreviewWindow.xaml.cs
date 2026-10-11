using System.ComponentModel;
using System.Windows;
using Lertaro.App.ViewModels.Settings.QuickPanel;

namespace Lertaro.App.Views.Settings.QuickPanel;

public partial class QuickPanelSizePreviewWindow : Window
{
    private QuickPanelWindowSizeSettingsViewModel? _model;

    public QuickPanelSizePreviewWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ContentRendered += (_, _) => ApplySize();
        Closed += (_, _) =>
        {
            _model?.PropertyChanged -= OnSizeChanged;
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _model?.PropertyChanged -= OnSizeChanged;
        _model = e.NewValue as QuickPanelWindowSizeSettingsViewModel;
        _model?.PropertyChanged += OnSizeChanged;
        ApplySize();
    }

    private void OnSizeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QuickPanelWindowSizeSettingsViewModel.Width)
            or nameof(QuickPanelWindowSizeSettingsViewModel.Height))
            ApplySize();
    }

    private void ApplySize()
    {
        if (_model == null) return;
        // Native window sizing replaces WPF Width/Height bindings after Show().
        Width = _model.Width;
        Height = _model.Height;
    }
}
