using Lertaro.Core.Services.Everything;
using Lertaro.Core.Services.Search;
using Lertaro.Core.Services;
using Lertaro.Core.IndexV2.Space;
using Lertaro.Core.Wire;
using System.IO.Pipes;
using System.Security.Principal;

namespace Lertaro.Core.Tests.Services.Everything;

[TestClass]
public class EverythingSearchDataProviderTests
{
    [TestMethod]
    public void RunCountTracking_MaintainsStateProperly()
    {
        using var searchService = new SearchService();
        var provider = new EverythingSearchDataProvider(searchService);

        var file = @"C:\Tools\tool.exe";
        Assert.AreEqual(0u, provider.GetRunCount(file));

        provider.SetRunCount(file, 5);
        Assert.AreEqual(5u, provider.GetRunCount(file));

        var next = provider.IncrementRunCount(file);
        Assert.AreEqual(6u, next);
        Assert.AreEqual(6u, provider.GetRunCount(file));
    }

    [TestMethod]
    public void RunCountTracking_PastTheEntryCap_StoresNoNewNames()
    {
        // The IPC surface takes these from any local process, so an unbounded map is a memory-exhaustion
        // vector; past the cap a name nobody has run through Lertaro is dropped rather than kept.
        using var searchService = new SearchService();
        var provider = new EverythingSearchDataProvider(searchService);

        for (var i = 0; i < EverythingSearchDataProvider.MaxRunHistoryEntries; i++)
            provider.SetRunCount($@"C:\Tools\tool{i}.exe", 1);

        var latecomer = @"C:\Tools\latecomer.exe";
        provider.SetRunCount(latecomer, 7);
        Assert.AreEqual(0u, provider.GetRunCount(latecomer));
        Assert.AreEqual(0u, provider.IncrementRunCount(latecomer));

        var alreadyStored = @"C:\Tools\tool0.exe";
        provider.SetRunCount(alreadyStored, 9);
        Assert.AreEqual(9u, provider.GetRunCount(alreadyStored), "an entry already in the map is still updated");
    }

    [TestMethod]
    public async Task SpaceQuery_UnindexedFolder_ReturnsEmptyResultThroughWire()
    {
        // Use a fresh engine and an actual wire round trip, not the machine's installed service.
        // The installed service may still use the previous protocol while a new build is tested.
        using var engine = new SearchEngine();
        using var stream = new MemoryStream();
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(stream, new SearchRequestMessage
        {
            Id = SearchRequestId.GetSpaceEntries,
            Drive = @"Z:\NonExistentDriveXYZ\",
            SpaceOptions = SpaceEntryFilters.IncludeSystemItems | SpaceEntryFilters.SkipCallerRowAccessChecks
        });
        stream.Position = 0;
        var request = await SearchRequestBinarySerializer.ReadSearchRequestAsync(stream);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeName = "LertaroEmptySpace_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        await Task.WhenAll(client.WriteAsync(new byte[] { 1 }, timeout.Token).AsTask(), server.ReadExactlyAsync(new byte[1], timeout.Token).AsTask());
        using var visibility = CallerVisibility.ForClient(server);
        var result = await UsnServicePipeRequestProcessor.ProcessAsync(engine, request, timeout.Token, server, visibility);

        Assert.AreEqual(PipeResponseKind.SpaceEntries, result.Kind);
        Assert.IsEmpty(result.SpaceEntries!);
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_RootDrivesQuery_ReturnsDrivesList()
    {
        using var searchService = new SearchService();
        var provider = new EverythingSearchDataProvider(searchService);

        var request = new EverythingQueryRequest(
            ReplyHwnd: IntPtr.Zero,
            ReplyCopyDataMessage: 0,
            SearchFlags: 0,
            Offset: 0,
            MaxResults: 100,
            RequestFlags: EverythingIpcConstants.RequestFileName | EverythingIpcConstants.RequestPath,
            SortType: 0,
            SearchString: "root:",
            IsUnicode: true,
            IsQuery2: true);

        var result = await provider.ExecuteQueryAsync(request);

        Assert.IsNotNull(result);
        Assert.IsGreaterThanOrEqualTo(1u, result.TotalItems);
        Assert.IsTrue(result.Items.Any(i => i.IsDrive));
    }
}
