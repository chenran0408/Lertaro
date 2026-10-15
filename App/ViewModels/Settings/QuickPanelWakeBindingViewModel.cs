using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

public sealed record WakeChoice<T>(T Value, string Label);

public sealed class QuickPanelWakeBindingViewModel : ViewModelBase
{
    private readonly QuickPanelWakeBinding _binding;
    public QuickPanelWakeBindingViewModel(QuickPanelWakeBinding binding)
    {
        _binding = binding.Copy();
        ToggleEditCommand = new RelayCommand(() => IsEditing = !IsEditing);
    }
    public QuickPanelWakeBinding Export() => _binding.Copy();
    public string Id => _binding.Id;
    public QuickPanelWakeKind Kind => _binding.Kind;
    public bool IsKeyboard => Kind == QuickPanelWakeKind.Keyboard;
    public bool IsHold => Kind == QuickPanelWakeKind.Hold;
    public bool IsCircle => Kind == QuickPanelWakeKind.Circle;
    public bool IsButton => Kind is QuickPanelWakeKind.Hold or QuickPanelWakeKind.Click;
    public bool Enabled { get => _binding.Enabled; set { if (_binding.Enabled == value) return; _binding.Enabled = value; Changed(nameof(Enabled)); } }
    public string Key { get => _binding.Key; set { if (_binding.Key == value) return; _binding.Key = value; Changed(nameof(Key)); } }
    public QuickPanelWakeButton Button { get => _binding.Button; set { if (_binding.Button == value) return; _binding.Button = value; Changed(nameof(Button)); } }
    public int HoldMilliseconds { get => _binding.HoldMilliseconds; set { var next = Math.Clamp(value, 300, 2000); if (_binding.HoldMilliseconds == next) return; _binding.HoldMilliseconds = next; Changed(nameof(HoldMilliseconds)); } }
    public int MovementTolerance { get => _binding.MovementTolerance; set { var next = Math.Clamp(value, 3, 30); if (_binding.MovementTolerance == next) return; _binding.MovementTolerance = next; Changed(nameof(MovementTolerance)); } }
    public int CircleTurns { get => _binding.CircleTurns; set { var next = Math.Clamp(value, 1, 3); if (_binding.CircleTurns == next) return; _binding.CircleTurns = next; Changed(nameof(CircleTurns)); } }
    public QuickPanelCircleDirection CircleDirection { get => _binding.CircleDirection; set { if (_binding.CircleDirection == value) return; _binding.CircleDirection = value; Changed(nameof(CircleDirection)); } }
    public int MinimumRadius { get => _binding.MinimumRadius; set { var next = Math.Clamp(value, 12, 150); if (_binding.MinimumRadius == next) return; _binding.MinimumRadius = next; Changed(nameof(MinimumRadius)); } }
    public int MaximumRadius { get => _binding.MaximumRadius; set { var next = Math.Clamp(value, 30, 500); if (_binding.MaximumRadius == next) return; _binding.MaximumRadius = next; Changed(nameof(MaximumRadius)); } }
    private bool _isEditing;
    public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }
    public ICommand ToggleEditCommand { get; }
    public string Name => TranslationManager.Instance["Wake_" + Kind];
    public string DelayLabel => string.Format(TranslationManager.Instance["Wake_Seconds"], HoldMilliseconds / 1000d);
    public string Summary => Kind switch
    {
        QuickPanelWakeKind.Keyboard => (string.IsNullOrWhiteSpace(Key) ? TranslationManager.Instance["Wake_UnsetKey"] : HotkeyStringFormat.ToDisplayText(Key))
            + " → " + TranslationManager.Instance["Wake_TogglePanel"],
        QuickPanelWakeKind.Hold => string.Format(TranslationManager.Instance["Wake_HoldSummary"], ButtonLabel, DelayLabel),
        QuickPanelWakeKind.Click => string.Format(TranslationManager.Instance["Wake_ClickSummary"], ButtonLabel),
        _ => string.Format(TranslationManager.Instance["Wake_CircleSummary"], TranslationManager.Instance["Wake_Direction" + CircleDirection], CircleTurns)
    };
    private string ButtonLabel => TranslationManager.Instance["Wake_Button" + Button];
    public IReadOnlyList<WakeChoice<QuickPanelWakeButton>> Buttons => Enum.GetValues<QuickPanelWakeButton>()
        .Where(b => IsHold || b != QuickPanelWakeButton.Right).Select(b => new WakeChoice<QuickPanelWakeButton>(b, TranslationManager.Instance["Wake_Button" + b])).ToArray();
    public IReadOnlyList<WakeChoice<QuickPanelCircleDirection>> Directions => Enum.GetValues<QuickPanelCircleDirection>()
        .Select(d => new WakeChoice<QuickPanelCircleDirection>(d, TranslationManager.Instance["Wake_Direction" + d])).ToArray();
    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(DelayLabel));
        OnPropertyChanged(nameof(Buttons)); OnPropertyChanged(nameof(Directions));
    }
    private void Changed(string name)
    {
        OnPropertyChanged(name); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(DelayLabel));
    }
}
