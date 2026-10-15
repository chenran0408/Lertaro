using Lertaro.App.ViewModels.Settings.Plugins;
using System.IO;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass, DoNotParallelize]
public sealed class PluginPreviewSessionTests
{
    private static PluginInfoViewModel MakePlugin(Func<IReadOnlyDictionary<string, object?>, Task> preview, Action rollback)
    {
        var field = new PluginConfigFieldViewModel("preview", new PluginConfigField { Key = "Color", FieldType = ConfigFieldType.Text, DefaultValue = "#000000" }, new UserSettings());
        var plugin = new PluginInfoViewModel("Preview", "1", "Preview.dll", "1", [], [field], onRollback: rollback, onPreviewAsync: preview);
        plugin.IsConfigTab = true;
        field.Value = "#123456";
        return plugin;
    }

    [TestMethod]
    public async Task PreviewUsesDraftWithoutCommittingIt()
    {
        object? received = null;
        var plugin = MakePlugin(values => { received = values["Color"]; return Task.CompletedTask; }, () => { });
        var session = new PluginPreviewSession(plugin);
        await session.PreviewAsync();
        Assert.AreEqual("#123456", received);
        Assert.IsTrue(plugin.HasPendingConfigEdits);
        Assert.IsTrue(session.IsActive);
    }

    [TestMethod]
    public async Task ClosingDuringPreparationRestoresLateResult()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restores = 0;
        var session = new PluginPreviewSession(MakePlugin(_ => completion.Task, () => restores++));
        var preview = session.PreviewAsync();
        Assert.IsTrue(session.IsBusy);
        session.Close();
        completion.SetResult();
        await preview;
        Assert.AreEqual(1, restores);
        Assert.IsFalse(session.IsActive);
        Assert.IsFalse(session.IsBusy);
    }

    [TestMethod]
    public async Task UndoDuringPreparationDiscardsLatePreviewAndDraft()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restores = 0;
        var plugin = MakePlugin(_ => completion.Task, () => restores++);
        var session = new PluginPreviewSession(plugin);
        var preview = session.PreviewAsync();
        session.Undo();
        completion.SetResult();
        await preview;
        Assert.IsFalse(plugin.HasPendingConfigEdits);
        Assert.AreEqual("#000000", plugin.ConfigFields[0].Value);
        Assert.IsFalse(session.IsActive);
        Assert.IsGreaterThanOrEqualTo(1, restores);
    }

    [TestMethod]
    public async Task FailedPreviewRestoresAndPropagatesFailure()
    {
        var restores = 0;
        var session = new PluginPreviewSession(MakePlugin(_ => Task.FromException(new IOException("Failed")), () => restores++));
        await Assert.ThrowsAsync<IOException>(() => session.PreviewAsync());
        Assert.AreEqual(1, restores);
        Assert.IsFalse(session.IsBusy);
        Assert.IsFalse(session.IsActive);
    }

    [TestMethod]
    public async Task RepeatedPreviewDoesNotStartTwoPreparations()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var session = new PluginPreviewSession(MakePlugin(_ => { started++; return completion.Task; }, () => { }));
        var first = session.PreviewAsync();
        await session.PreviewAsync();
        Assert.AreEqual(1, started);
        completion.SetResult();
        await first;
        Assert.IsTrue(session.IsActive);
    }
}
