using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.Theme;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

public sealed class BackgroundSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _settings;
    private BackgroundSettings _draft;
    private BackgroundSettings _committed;
    private string _status = "";
    private int _imageCheck;
    private System.Windows.Window? _materialPreview;

    public BackgroundSettingsViewModel(UserSettings settings)
    {
        _settings = settings;
        _committed = _draft = (settings.Background ?? new()).Normalize();
        BrowseCommand = new RelayCommand(Browse);
        ResetCommand = new RelayCommand(() => Update(new BackgroundSettings()));
        CancelCommand = new RelayCommand(() => Update(_committed));
        PickColorCommand = new RelayCommand<string>(PickColor);
        PreviewMaterialCommand = new RelayCommand(PreviewMaterial, () => IsMaterialSupported);
        CheckImageAsync();
    }

    public ICommand BrowseCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand PickColorCommand { get; }
    public ICommand PreviewMaterialCommand { get; }
    public int Kind { get => (int)_draft.Kind; set => Update(_draft with { Kind = (BackgroundKind)value }); }
    public string ImagePath { get => _draft.ImagePath; set => Update(_draft with { ImagePath = value }); }
    public string FirstColor { get => _draft.FirstColor; set { if (BackgroundSettings.ValidColor(value)) Update(_draft with { FirstColor = value }); } }
    public string SecondColor { get => _draft.SecondColor; set { if (BackgroundSettings.ValidColor(value)) Update(_draft with { SecondColor = value }); } }
    public double Angle { get => _draft.Angle; set => Update(_draft with { Angle = value }); }
    public double Blur { get => _draft.Blur; set => Update(_draft with { Blur = value }); }
    public double Tint { get => _draft.Tint * 100; set => Update(_draft with { Tint = value / 100 }); }
    public bool FitImage { get => _draft.FitImage; set => Update(_draft with { FitImage = value }); }
    public bool IsImage => _draft.Kind == BackgroundKind.Image;
    public bool IsGradient => _draft.Kind == BackgroundKind.Gradient;
    public bool IsColor => _draft.Kind is BackgroundKind.Solid or BackgroundKind.Gradient;
    public bool IsCustom => _draft.Kind != BackgroundKind.Theme;
    public bool IsMaterial => _draft.Kind == BackgroundKind.Material;
    public bool IsMaterialSupported => Helpers.Visuals.NativeMaterialHost.IsSupported;
    public int Material { get => (int)_draft.Material; set => Update(_draft with { Material = (WindowMaterial)value }); }
    public string MaterialHint => TranslationManager.Instance[IsMaterialSupported ? "Background_MaterialHint" : "Background_MaterialUnsupported"];
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private void Update(BackgroundSettings value)
    {
        _draft = value.Normalize();
        OnPropertyChanged("");
        BackgroundManager.Preview(_draft);
        if (!IsMaterial) _materialPreview?.Close();
        CheckImageAsync();
    }

    private async void CheckImageAsync()
    {
        var generation = ++_imageCheck;
        Status = "";
        if (!IsImage) return;
        var image = await BackgroundImageCache.LoadAsync(ImagePath);
        if (generation == _imageCheck && image == null)
            Status = TranslationManager.Instance["Background_ImageUnavailable"];
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = TranslationManager.Instance["Background_ImageFilter"],
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault()) == true)
            Update(_draft with { ImagePath = dialog.FileName, Kind = BackgroundKind.Image });
    }

    private void PreviewMaterial()
    {
        if (_materialPreview != null) { _materialPreview.Activate(); return; }
        var window = new Views.Settings.MaterialPreviewWindow
        {
            Owner = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault()
        };
        _materialPreview = window;
        window.Closed += (_, _) => _materialPreview = null;
        window.Show();
    }

    private void PickColor(string which)
    {
        var owner = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        var color = Helpers.Visuals.ColorPickerDialog.Choose(owner, which == "Second" ? SecondColor : FirstColor);
        if (color != null)
        {
            if (which == "Second") SecondColor = color; else FirstColor = color;
        }
    }

    public void Stage() => _settings.Background = _draft;
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(MaterialHint));
        CheckImageAsync();
    }
    public void RejectSave() => _settings.Background = _committed;
    public void Accept()
    {
        _committed = _draft;
        BackgroundManager.EndPreview();
    }
    public void Cleanup()
    {
        ++_imageCheck;
        _materialPreview?.Close();
        _settings.Background = _committed;
        BackgroundManager.EndPreview();
    }
}
