namespace Lertaro.Core;

public sealed class QuickPanelItemLabel
{
    public string DisplayName { get; set; } = string.Empty;
    public bool HideName { get; set; }
    public QuickPanelItemLabel Clone() => new() { DisplayName = DisplayName, HideName = HideName };
}
