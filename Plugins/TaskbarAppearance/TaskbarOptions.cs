namespace Lertaro.Plugins.TaskbarAppearance;

internal sealed class TaskbarOptions
{
    public bool Enabled { get; set; }
    public string ExecutablePath { get; set; } = "";
    public string Style { get; set; } = "clear";
    public string Color { get; set; } = "#000000";
    public int Opacity { get; set; }
    public bool ShowLine { get; set; }
    public bool ShowPeek { get; set; } = true;
    public bool UseChinese { get; set; } = true;
    public List<TaskbarRule> Rules { get; set; } = [];
}

internal sealed class TaskbarRule
{
    public bool Enabled { get; set; } = true;
    public string Situation { get; set; } = "maximized_window_appearance";
    public string Style { get; set; } = "normal";
    public string Color { get; set; } = "#000000";
    public int Opacity { get; set; }
}
