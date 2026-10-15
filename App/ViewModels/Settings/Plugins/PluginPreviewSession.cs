namespace Lertaro.App.ViewModels.Settings.Plugins;

// Owns preview lifetime separately from the view, particularly when asynchronous preparation outlives it.
internal sealed class PluginPreviewSession(PluginInfoViewModel plugin)
{
    private int _revision;
    internal bool IsBusy { get; private set; }
    internal bool IsActive { get; private set; }
    internal bool IsClosed { get; private set; }

    internal async Task PreviewAsync()
    {
        if (IsBusy || IsClosed || plugin.OnPreviewAsync == null) return;
        IsBusy = true;
        var revision = _revision;
        try
        {
            await plugin.OnPreviewAsync(plugin.ConfigFields.ToDictionary(f => f.SchemaField.Key, f => f.Value));
            if (IsClosed || revision != _revision) plugin.OnRollback?.Invoke();
            else IsActive = true;
        }
        catch
        {
            IsActive = false;
            plugin.OnRollback?.Invoke();
            throw;
        }
        finally { IsBusy = false; }
    }

    internal void Undo()
    {
        ++_revision;
        IsActive = false;
        plugin.RollbackConfig();
    }

    internal void Close()
    {
        if (IsClosed) return;
        IsClosed = true;
        ++_revision;
        if (IsActive) plugin.OnRollback?.Invoke();
        IsActive = false;
    }
}
