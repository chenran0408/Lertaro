using System.IO;
using System.Security.Cryptography;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

internal static class FlowRuntimeIntegrity
{
    internal const string NodeVersion = "22.23.3";
    internal const string PythonVersion = "3.13.16";

    // Pinned from nodejs.org/dist/v22.23.3/SHASUMS256.txt and python.org's
    // python-3.13.16-embed-{amd64,arm64}.zip.sigstore message digests (2026-10-16).
    internal static string NodeHash(bool arm64) => arm64
        ? "33DAD22E4CEF5EE8F9FBB1B0D037FDACD0E56D12A4580F0D63F68B894DEAB535"
        : "2B0FF57B049CDA1BBCEA2240EEC20467018713C1EFE1F7360C2681859B90ED71";
    internal static string PythonHash(bool arm64) => arm64
        ? "790D097697A2020477549A6764D89D22194C88D6E42CAEBCB2DB63791E004C94"
        : "97DAE5274CC54867065E8D5A3226E48C35017ED332A0FDB0E27D5B5821961297";

    internal static void VerifyArchive(string archive, string expectedHash)
    {
        using var stream = File.OpenRead(archive);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(expectedHash)))
            throw new InvalidDataException("运行环境下载校验失败，请重新下载。");
    }
}
