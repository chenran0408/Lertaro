using System.IO;
using System.Windows.Shell;
using Lertaro.Core;
using Lertaro.Core.Settings;
using Application = System.Windows.Application;
using JumpList = System.Windows.Shell.JumpList;

namespace Lertaro.App.Services.TaskbarIntegration;

/// <summary>
/// B1: Windows taskbar JumpList with "Recent searches" (from SearchHistoryStore, top 10 distinct
/// keywords) and "Favorite directories" (from UserSettings.Favorites, top 10) categories. Clicking
/// an entry routes through <c>lertaro://search/{query}</c> into the main window. Rebuilt on
/// startup and on single-instance re-activation.
/// </summary>
internal static class JumpListService
{
    private const int MaxPerCategory = 10;

    public static void Refresh()
    {
        try
        {
            var jumpList = new JumpList();
            var hasItems = false;

            // Recent searches: distinct keywords, most recent first.
            var seenKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in SearchHistoryStore.GetEntries())
            {
                if (string.IsNullOrWhiteSpace(entry.Keyword) ||
                    !seenKeywords.Add(entry.Keyword)) continue;
                jumpList.JumpItems.Add(new JumpTask
                {
                    Title = entry.Keyword,
                    Description = string.Format(
                        TranslationManager.Instance["JumpList_RecentSearch"], entry.Keyword),
                    ApplicationPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "",
                    Arguments = $"lertaro://search/{Uri.EscapeDataString(entry.Keyword)}",
                    CustomCategory = TranslationManager.Instance["JumpList_CategorySearches"],
                });
                hasItems = true;
                if (seenKeywords.Count >= MaxPerCategory) break;
            }

            // Favorite directories: the first 10 favorites with a path.
            var settings = UserSettings.Load();
            var favCount = 0;
            foreach (var favorite in settings.Favorites)
            {
                if (string.IsNullOrWhiteSpace(favorite.Path) || !Directory.Exists(favorite.Path)) continue;
                jumpList.JumpItems.Add(new JumpTask
                {
                    Title = string.IsNullOrEmpty(favorite.Name) ? favorite.Path : favorite.Name,
                    Description = favorite.Path,
                    ApplicationPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "",
                    Arguments = $"lertaro://search/{Uri.EscapeDataString(favorite.Path)}",
                    CustomCategory = TranslationManager.Instance["JumpList_CategoryFavorites"],
                });
                hasItems = true;
                if (++favCount >= MaxPerCategory) break;
            }

            if (!hasItems) return;
            JumpList.SetJumpList(Application.Current, jumpList);
        }
        catch (Exception ex)
        {
            Logger.Log($"[JumpList] Refresh failed: {ex.Message}", LogLevel.Warn);
        }
    }
}
