using System.Diagnostics;
using System.IO;
using System.Text;
using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class AppSingleInstanceTests
{
    [TestMethod]
    public void AcquireMutex_TakesOwnershipOfAbandonedMutex()
    {
        var mutexName = $@"Local\Lertaro.Tests.{Guid.NewGuid():N}";
        // Keep the named kernel object alive while its owning process is terminated.
        using var mutexAnchor = new Mutex(false, mutexName, out var anchorCreatedNew);
        Assert.IsTrue(anchorCreatedNew, "The test must create a unique mutex before starting its owner process.");

        var script = $"$mutex = [System.Threading.Mutex]::OpenExisting('{mutexName}'); " +
                     "if (-not $mutex.WaitOne(0)) { throw 'Could not acquire test mutex.' }; " +
                     "[Console]::Out.WriteLine('READY'); [Console]::ReadLine() | Out-Null";
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo(powershellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedScript);

        using var ownerProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the mutex owner process.");
        try
        {
            var ready = ownerProcess.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            Assert.AreEqual("READY", ready,
                $"The owner process did not acquire the test mutex. {ownerProcess.StandardError.ReadToEnd()}");

            ownerProcess.Kill(entireProcessTree: true);
            ownerProcess.WaitForExit();

            using var acquiredMutex = AppSingleInstance.AcquireMutex(mutexName, out var createdNew);

            Assert.IsTrue(createdNew, "An abandoned mutex should be treated as owned by this instance.");
            acquiredMutex.ReleaseMutex();
        }
        finally
        {
            if (!ownerProcess.HasExited)
            {
                ownerProcess.Kill(entireProcessTree: true);
                ownerProcess.WaitForExit();
            }
        }
    }

    [TestMethod]
    public void AcquireMutex_DoesNotClaimMutexHeldByLiveInstance()
    {
        var mutexName = $@"Local\Lertaro.Tests.{Guid.NewGuid():N}";
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Mutex? liveMutex = null;
        Exception? workerException = null;
        var ownerThread = new Thread(() =>
        {
            try
            {
                liveMutex = new Mutex(true, mutexName, out var createdNew);
                if (!createdNew)
                    throw new InvalidOperationException("The test mutex was unexpectedly reused.");
            }
            catch (Exception ex)
            {
                workerException = ex;
            }
            finally
            {
                ready.Set();
            }

            release.Wait();
            if (workerException == null)
                liveMutex!.ReleaseMutex();
        });
        ownerThread.Start();
        ready.Wait();

        try
        {
            Assert.IsNull(workerException, workerException?.ToString());
            using var secondMutex = AppSingleInstance.AcquireMutex(mutexName, out var createdNew);

            Assert.IsFalse(createdNew, "A live instance must remain the sole mutex owner.");
        }
        finally
        {
            release.Set();
            ownerThread.Join();
            liveMutex?.Dispose();
        }
    }
}
