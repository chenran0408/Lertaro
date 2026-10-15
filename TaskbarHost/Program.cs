using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lertaro.TaskbarHost;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        HostArguments options;
        try { options = HostArguments.Parse(args); }
        catch (ArgumentException) { return 2; }
        try
        {
            using var parent = Process.GetProcessById(options.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != options.ParentStarted) return 3;
            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, $"Local\\Lertaro.Taskbar.{options.ParentId}");
            // The plugin checks for competing tools too. Recheck immediately before starting the native engine.
            var existing = Process.GetProcessesByName("TranslucentTB");
            try
            {
                if (existing.Length != 0) throw new InvalidOperationException("Another TranslucentTB instance is running.");
            }
            finally { foreach (var process in existing) process.Dispose(); }
            using var engine = Process.Start(new ProcessStartInfo(options.Engine)
            {
                WorkingDirectory = Path.GetDirectoryName(options.Engine)!,
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new IOException("Could not start the taskbar engine.");
            try
            {
                await Task.Delay(1200);
                if (engine.HasExited) throw new IOException($"Taskbar engine exited with code {engine.ExitCode}.");
                File.WriteAllText(options.StatusFile, "ready");
                while (!parent.HasExited && !engine.HasExited && !stop.WaitOne(0)) await Task.Delay(250);
                if (engine.HasExited && !parent.HasExited && !stop.WaitOne(0))
                    File.WriteAllText(options.StatusFile, $"error:Taskbar engine exited with code {engine.ExitCode}.");
            }
            finally
            {
                if (!engine.HasExited)
                {
                    // WM_CLOSE follows upstream's normal Exit path and restores the taskbar. Never kill another tool.
                    EnumWindows((window, _) =>
                    {
                        GetWindowThreadProcessId(window, out var process);
                        if (process == engine.Id) PostMessage(window, 0x0010, 0, 0);
                        return true;
                    }, 0);
                    await Task.WhenAny(engine.WaitForExitAsync(), Task.Delay(10000));
                }
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            try { File.WriteAllText(options.StatusFile, "error:" + ex.Message); }
            catch (Exception statusError) when (statusError is IOException or UnauthorizedAccessException) { }
            return 1;
        }
    }

    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
