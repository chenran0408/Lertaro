using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.QuickPanel;

public sealed class QuickPanelWindowSizeSettingsViewModel : ViewModelBase
{
    private bool _useCustomWindowSize;
    private double _width;
    private double _height;

    public QuickPanelWindowSizeSettingsViewModel(QuickPanelSettings settings)
    {
        _useCustomWindowSize = settings.UseCustomWindowSize;
        _width = Normalize(settings.WindowWidth, 280, 640);
        _height = Normalize(settings.WindowHeight, 200, 480);
    }

    public bool UseCustomWindowSize
    {
        get => _useCustomWindowSize;
        set => SetProperty(ref _useCustomWindowSize, value);
    }

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, Normalize(value, 280, 640));
    }

    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, Normalize(value, 200, 480));
    }

    public void Save(QuickPanelSettings settings)
    {
        settings.UseCustomWindowSize = UseCustomWindowSize;
        settings.WindowWidth = Width;
        settings.WindowHeight = Height;
    }

    private static double Normalize(double value, double minimum, double fallback)
        => double.IsFinite(value) ? Math.Round(Math.Clamp(value, minimum, 4096)) : fallback;
}
