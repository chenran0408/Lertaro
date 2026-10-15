using System.Diagnostics;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class UpdaterTaskbarHostTests
{
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public async Task WaitsOnlyForThisInstallAndRefusesToCopyIfItsHostCannotExit(bool sameInstall, bool canExit)
    {
        var root = Directory.CreateTempSubdirectory("LertaroTaskbarWait-").FullName;
        try
        {
            var runner = Path.Combine(root, "run.ps1");
            File.WriteAllText(runner, """
                param($Updater,$Target,$FakePath,$CanExit,$ExpectedWait,$ExpectedThrow)
                $ErrorActionPreference = 'Stop'
                . $Updater
                $script:Fake = [PSCustomObject]@{Path=$FakePath; CanExit=[bool]::Parse($CanExit); Waited=$false; Disposed=$false}
                $script:Fake | Add-Member ScriptMethod WaitForExit { param($Timeout) $this.Waited=$true; return $this.CanExit }
                $script:Fake | Add-Member ScriptMethod Dispose { $this.Disposed=$true }
                function Get-Process { param($Name,$ErrorAction) return $script:Fake }
                $threw = $false
                try { Wait-LertaroTaskbarHostExit -TargetRoot $Target -TimeoutMs 1 }
                catch { $threw=$true }
                if ($threw -ne [bool]::Parse($ExpectedThrow)) { throw 'Wrong timeout behavior.' }
                if ($script:Fake.Waited -ne [bool]::Parse($ExpectedWait)) { throw 'Waited for the wrong installation.' }
                if (!$script:Fake.Disposed) { throw 'Process handle leaked.' }
                """);
            var target = Path.Combine(root, "app");
            var path = Path.Combine(sameInstall ? target : Path.Combine(root, "other"), "Lertaro.TaskbarHost.exe");
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", runner, Path.Combine(AppContext.BaseDirectory, "portable-updater.ps1"), target, path, canExit.ToString(), sameInstall.ToString(), (sameInstall && !canExit).ToString() })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.AreEqual(0, process.ExitCode, await output + await error);
        }
        finally { Directory.Delete(root, true); }
    }
}
