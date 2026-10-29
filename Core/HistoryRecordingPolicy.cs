namespace Lertaro.Core;

// Keep the two history stores consistent: disabling a store blocks all recording, including updates
// to entries that already exist.
internal static class HistoryRecordingPolicy
{
    public static bool ShouldRecord(bool enabled) => enabled;
}
