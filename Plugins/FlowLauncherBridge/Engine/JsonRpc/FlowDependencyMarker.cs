using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

internal static class FlowDependencyMarker
{
    internal static string Fingerprint(string runtime, params string[] manifests)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFullPath(runtime)));
        foreach (var file in manifests)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file) + "\0"));
            hash.AppendData(File.Exists(file) ? File.ReadAllBytes(file) : []);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static bool Matches(string marker, string fingerprint) =>
        File.Exists(marker) && File.ReadAllText(marker) == fingerprint;
}
