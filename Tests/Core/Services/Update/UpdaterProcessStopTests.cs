using System.Diagnostics;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class UpdaterProcessStopTests
{
    [TestMethod]
    [DataRow("ServiceStopped", false, 0, 1)]
    [DataRow("ServicePending", true, 0, 1)]
    [DataRow("AlreadyExited", false, 0, 0)]
    [DataRow("GracefulApp", false, 0, 1)]
    [DataRow("ExitRace", false, 1, 2)]
    [DataRow("DeniedLive", true, 1, 1)]
    [DataRow("Hook", false, 1, 2)]
    [DataRow("OtherInstall", false, 0, 0)]
    public Task StoppingProcesses_HandlesServiceShutdownAndExitRacesWithoutHidingRealDenials(
        string mode, bool expectedThrow, int kills, int waits) => RunAsync(mode, expectedThrow, kills, waits);

    [TestMethod]
    [DataRow("ServiceAck", false)]
    [DataRow("ServiceAckTimeout", true)]
    public Task AcceptedServiceStop_WaitsForStoppedAndDisposesController(string mode, bool expectedThrow) =>
        RunAsync(mode, expectedThrow, 0, 1);

    private static async Task RunAsync(string mode, bool expectedThrow, int kills, int waits)
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdaterStopRace_").FullName;
        try
        {
            var runner = Path.Combine(root, "run.ps1");
            File.WriteAllText(runner, """
                param($Updater,$Root,$Mode,$ExpectedThrow,$Kills,$Waits)
                $ErrorActionPreference='Stop'
                . $Updater
                $script:fake=[PSCustomObject]@{
                    Path=(Join-Path $Root 'Lertaro.App.exe'); ProcessName='Lertaro.App'; Id=123; SessionId=1
                    HasExited=($Mode -eq 'AlreadyExited'); Kills=0; Waits=0; Disposed=$false; Mode=$Mode
                }
                if($Mode -like 'Service*'){$script:fake.ProcessName='Lertaro.Service';$script:fake.SessionId=0}
                if($Mode -eq 'OtherInstall'){$script:fake.Path=Join-Path (Join-Path $Root 'other') 'Lertaro.App.exe'}
                $script:fake | Add-Member ScriptMethod WaitForExit {
                    param($Timeout)
                    $this.Waits++
                    if($this.Mode -in 'ServiceStopped','GracefulApp'){$this.HasExited=$true}
                    return $this.HasExited
                }
                $script:fake | Add-Member ScriptMethod Kill {
                    $this.Kills++
                    if($this.Mode -eq 'ExitRace'){$this.HasExited=$true;throw [ComponentModel.Win32Exception]::new(5)}
                    if($this.Mode -eq 'DeniedLive'){throw [ComponentModel.Win32Exception]::new(5)}
                    $this.HasExited=$true
                }
                $script:fake | Add-Member ScriptMethod WaitForStatus {
                    param($Status,$Timeout)
                    $this.Waits++
                    if($Status -ne 'Stopped'){throw 'Wrong target status'}
                    if($this.Mode -eq 'ServiceAckTimeout'){throw [TimeoutException]::new('service still stopping')}
                }
                $script:fake | Add-Member ScriptMethod Dispose {$this.Disposed=$true}
                function Get-Process {param($Name,$ErrorAction) return $script:fake}
                function Get-Service {param($Name,$ErrorAction) return $script:fake}
                function Fake-Sc {param($Action,$Name) $global:LASTEXITCODE=0}
                $threw=$false
                try {
                    if($Mode -like 'ServiceAck*'){Stop-LertaroService 'Fake-Sc' 100 1}
                    else {Stop-LertaroProcesses -TargetRoot $Root -TimeoutMs 100 -GraceMs 10}
                } catch {$threw=$true;$message=$_.Exception.Message}
                if($threw -ne [bool]::Parse($ExpectedThrow)){throw "Unexpected failure state: $threw / $message"}
                if($Mode -eq 'DeniedLive' -and $message -notlike '*PID 123*'){throw 'Denial omitted process identity'}
                if($script:fake.Kills -ne [int]$Kills -or $script:fake.Waits -ne [int]$Waits){throw "Unexpected process actions: $($script:fake.Kills) kills, $($script:fake.Waits) waits"}
                if(!$script:fake.Disposed){throw 'Process/controller handle was not disposed'}
                """);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", runner,
                Path.Combine(AppContext.BaseDirectory, "portable-updater.ps1"), root, mode, expectedThrow.ToString(), kills.ToString(), waits.ToString() })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.AreEqual(0, process.ExitCode, await output + await error);
        }
        finally { Directory.Delete(root, true); }
    }
}
