using System.Windows.Input;
using Lertaro.App.Helpers;
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

    public BackgroundSettingsViewModel(UserSettings settings)
    {
        _settings = settings;
        _committed = _draft = (settings.Background ?? new()).Normalize();
        BrowseCommand = new RelayCommand(Browse);
        ResetCommand = new RelayCommand(() => Update(new BackgroundSettings()));
        CancelCommand = new RelayCommand(() => Update(_committed));
        CheckImageAsync();
    }

    public ICommand BrowseCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand CancelCommand { get; }
    public bool UseImage { get => _draft.Kind == BackgroundKind.Image; set => Update(_draft with { Kind = value ? BackgroundKind.Image : BackgroundKind.Theme }); }
    public string ImagePath { get => _draft.ImagePath; set => Update(_draft with { ImagePath = value }); }
    public double Opacity { get => _draft.Opacity * 100; set => Update(_draft with { Opacity = value / 100 }); }
    public bool FitImage { get => _draft.FitImage; set => Update(_draft with { FitImage = value }); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private void Update(BackgroundSettings value)
    {
        var next = value.Normalize();
        var checkImage = next.ImagePath != _draft.ImagePath || next.Kind != _draft.Kind;
        _draft = next;
        OnPropertyChanged("");
        BackgroundManager.Preview(_draft);
        if (checkImage) CheckImageAsync();
    }

    private async void CheckImageAsync()
    {
        var generation = ++_imageCheck;
        Status = "";
        if (!UseImage) return;
        var image = await BackgroundImageCache.LoadAsync(ImagePath);
        if (generation == _imageCheck && image == null)
            Status = Services.TranslationManager.Instance["Background_ImageUnavailable"];
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Services.TranslationManager.Instance["Background_ImageFilter"],
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault()) == true)
            Update(_draft with { ImagePath = dialog.FileName, Kind = BackgroundKind.Image });
    }

    public void Stage() => _settings.Background = _draft;
    public void RefreshLanguage() => CheckImageAsync();
    public void RejectSave() => _settings.Background = _committed;
    public void Accept()
    {
        _committed = _draft;
        BackgroundManager.EndPreview();
    }
    public void Cleanup()
    {
        ++_imageCheck;
        _settings.Background = _committed;
        BackgroundManager.EndPreview();
    }
}
