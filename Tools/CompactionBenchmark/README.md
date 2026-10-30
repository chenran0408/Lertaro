# Concurrent compaction/search benchmark

Run on Windows with the .NET 10 SDK and the Rust/MSVC toolchain used by Core:

```powershell
dotnet run --project Tools/CompactionBenchmark -c Release -- 500000 30 1
```

The benchmark writes a synthetic 500,000-row index, warms search and compaction,
then starts a known-result query 5 ms after each real compaction begins. The optional third argument
sets how many added rows are folded into each round; use a larger value to measure a burst of pending
changes rather than a single-row update.
It checks the result remains visible and all appended rows survive on disk.
JSON reports total-query and first-result latency, save duration, total process
managed allocations, and process peak working set (including setup).

To compare an existing release without rebuilding its Core, build with
`-p:CoreAssembly=<absolute path to Lertaro.Core.dll>` and put that release's
`Lertaro.PluginSdk.dll` beside Core. Copy its `lertaro_index_store.dll` to the
benchmark output. Keep baseline/current output directories separate.

## Initial local observation (2026-10-30)

Same Windows x64 host, 500,000 synthetic ASCII file names, 30 rounds, warm cache.
Baseline: Core and native DLL extracted from the signed v6.0.0 x64 release.
Current: same native DLL with the new LiveIndex publication path.

| Metric | v6.0.0 | New path |
| --- | ---: | ---: |
| Query median | 138.90 ms | 3.30 ms |
| Query P95 | 219.43 ms | 30.50 ms |
| Query P99 / maximum | 222.51 ms | 82.61 ms |
| Save median | 150.27 ms | 154.39 ms |
| Save P95 | 183.32 ms | 223.66 ms |
| Process peak working set | 350.1 MiB | 354.0 MiB |
| Process allocations across rounds | 3,842,984,080 B | 3,843,404,480 B |
| Persisted rows | 500,030 | 500,030 |

This is not a general search throughput or memory-footprint claim. With only
30 queries, the empirical P99 is the maximum; rerun with more rounds and real
index distributions before defining an SLO. Search is faster during preparation,
but competes with compaction for CPU and can make saving slower.

## Concurrency boundary

Mutations, swaps and disposal serialize on the mutation gate. Compaction freezes
the existing overlay, builds and validates a candidate without excluding readers,
then takes the exclusive reader lock only for publication/open/swap. No second
full overlay or intermediate FileRecordStore is created.

Incoming mutations still wait for preparation; they resume against the new
snapshot after a successful save, or the original snapshot/overlay after failure.
The short publication step can still wait for existing streaming readers; a slow
consumer holding a search callback is a separate, pre-existing limitation.

Failure tests cover temporary writes, file replacement, candidate validation,
opening the published file, retry, concurrent search, and queued mutations.
