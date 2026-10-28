using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class AppSingleInstanceTests
{
    [TestMethod]
    public void AcquireMutex_TakesOwnershipOfAbandonedMutex()
    {
        var mutexName = $@"Local\Lertaro.Tests.{Guid.NewGuid():N}";
        Mutex? abandonedMutex = null;
        Exception? workerException = null;

        var ownerThread = new Thread(() =>
        {
            try
            {
                abandonedMutex = new Mutex(false, mutexName, out var createdNew);
                if (!createdNew)
                    throw new InvalidOperationException("The test mutex was unexpectedly reused.");
                if (!abandonedMutex.WaitOne(0))
                    throw new InvalidOperationException("The test thread could not acquire its new mutex.");
            }
            catch (Exception ex)
            {
                workerException = ex;
            }
        });
        ownerThread.Start();
        ownerThread.Join();
        var ownerThreadAliveAfterJoin = ownerThread.IsAlive;

        try
        {
            Assert.IsNull(workerException, workerException?.ToString());
            Assert.IsNotNull(abandonedMutex);

            using var acquiredMutex = AppSingleInstance.AcquireMutex(mutexName, out var createdNew);

            var ownershipProbe = "not needed";
            var probeAcquiredOwnership = false;
            if (!createdNew)
            {
                try
                {
                    probeAcquiredOwnership = acquiredMutex.WaitOne(0);
                    ownershipProbe = probeAcquiredOwnership ? "acquired" : "still owned by another thread";
                }
                catch (AbandonedMutexException)
                {
                    probeAcquiredOwnership = true;
                    ownershipProbe = "abandoned; probe took ownership";
                }
            }

            if (probeAcquiredOwnership)
                acquiredMutex.ReleaseMutex();

            Assert.IsTrue(createdNew,
                $"An abandoned mutex should be treated as owned by this instance. " +
                $"Owner thread alive after Join: {ownerThreadAliveAfterJoin}; follow-up WaitOne(0): {ownershipProbe}.");
            acquiredMutex.ReleaseMutex();
        }
        finally
        {
            abandonedMutex?.Dispose();
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
