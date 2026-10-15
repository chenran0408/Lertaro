using System.Diagnostics;
using System.Text.Json;

namespace Lertaro.Core.Hook.InlineSearch;

public sealed class QuickPanelWakeTestRequest
{
    public QuickPanelWakeBinding Binding { get; set; } = new();
    public bool AllowDesktop { get; set; } = true;
    public List<string> AllowedProcesses { get; set; } = new();
}

// Only the hook process owns this instance. IPC replaces immutable snapshots, never live UI objects.
internal sealed class QuickPanelWakeTestSession
{
    private static QuickPanelWakeTestSession? _current;
    internal readonly QuickPanelWakeTestRequest Request;
    private readonly long _deadline;
    internal QuickPanelWakeTestSession(QuickPanelWakeTestRequest request, long now)
    { Request = request; _deadline = now + Stopwatch.Frequency * 30; }
    internal bool IsActive(long now) => now < _deadline;
    internal static QuickPanelWakeTestSession? Current
    {
        get
        {
            var session = Volatile.Read(ref _current);
            return session?.IsActive(Stopwatch.GetTimestamp()) == true ? session : null;
        }
    }
    internal static bool Start(string? json)
    {
        if (json is null || json.Length > 64000) return false;
        try
        {
            var request = JsonSerializer.Deserialize<QuickPanelWakeTestRequest>(json);
            if (request?.Binding is null || QuickPanelWakeBindings.Validate(request.Binding) != null) return false;
            request.Binding.Enabled = true;
            request.AllowedProcesses ??= new();
            Volatile.Write(ref _current, new(request, Stopwatch.GetTimestamp()));
            return true;
        }
        catch (JsonException) { return false; }
    }
    internal static void Stop() => Volatile.Write(ref _current, null);
    internal static event Action<string, string>? Reported;
    internal static void Report(QuickPanelWakeTestSession session, string reason)
    {
        if (ReferenceEquals(Current, session)) Reported?.Invoke(session.Request.Binding.Id, reason);
    }
}
