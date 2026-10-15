using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lertaro.Plugins.TaskbarAppearance;

// Configuration interoperability with the independent bundled or user-selected native engine.
internal static class TaskbarConfiguration
{
    internal static readonly string[] Styles = ["normal", "clear", "opaque", "acrylic", "blur"];
    internal static readonly string[] Situations = ["visible_window_appearance", "maximized_window_appearance",
        "start_opened_appearance", "search_opened_appearance", "task_view_opened_appearance", "battery_saver_appearance"];

    public static string Build(string existing, TaskbarOptions options)
    {
        var root = string.IsNullOrWhiteSpace(existing) ? new JsonObject() : JsonNode.Parse(existing,
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new JsonException("Taskbar configuration must be an object.");
        if (!options.Enabled)
        {
            SetAppearance(root, "desktop_appearance", "normal", "#000000", 0, true);
            foreach (var situation in Situations) SetAppearance(root, situation, "normal", "#000000", 0, true, false);
        }
        else
        {
            SetAppearance(root, "desktop_appearance", options.Style, options.Color, options.Opacity, options.ShowLine, showPeek: options.ShowPeek);
            foreach (var situation in Situations)
                SetAppearance(root, situation, "normal", "#000000", 0, options.ShowLine, false, options.ShowPeek);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in options.Rules)
            {
                if (!Situations.Contains(rule.Situation, StringComparer.Ordinal) || !used.Add(rule.Situation))
                    throw new ArgumentException("Invalid or duplicate taskbar situation.");
                SetAppearance(root, rule.Situation, rule.Style, rule.Color, rule.Opacity, options.ShowLine, rule.Enabled, options.ShowPeek);
            }
            if (options.UseChinese) root["language"] = "zh-CN";
            if (options.IsBundled) root["hide_tray"] = true;
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void SetAppearance(JsonObject root, string key, string style, string color, int opacity, bool showLine, bool? enabled = null, bool showPeek = true)
    {
        if (!Styles.Contains(style, StringComparer.Ordinal)) throw new ArgumentException("Unknown taskbar style.");
        if (!Regex.IsMatch(color ?? "", "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant) || opacity is < 0 or > 100)
            throw new ArgumentException("Use a #RRGGBB color and an opacity between 0 and 100.");
        var appearance = root[key] as JsonObject ?? new JsonObject();
        if (root[key] is not JsonObject) root[key] = appearance;
        appearance["accent"] = style;
        // TranslucentTB uses #RRGGBBAA, not WPF's #AARRGGBB.
        appearance["color"] = color!.ToUpperInvariant() + ((int)Math.Round(opacity * 255.0 / 100)).ToString("X2");
        appearance["show_line"] = showLine;
        appearance["show_peek"] = showPeek;
        if (enabled.HasValue) appearance["enabled"] = enabled.Value;
    }
}
