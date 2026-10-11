using System.Windows.Input;
using Lertaro.App.Services;

namespace Lertaro.App.ViewModels.Settings;

public sealed class RunningProcessChoice : ViewModelBase
{
    internal RunningProcessChoice(RunningProcessInfo process) => Process = process;
    internal RunningProcessInfo Process { get; }
    public string Name => Process.Name;
    public string WindowTitle => Process.WindowTitle;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) CommandManager.InvalidateRequerySuggested();
        }
    }
}
