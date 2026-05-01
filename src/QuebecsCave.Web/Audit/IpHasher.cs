using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace QuebecsCave.Web.Audit;

internal static class IpHasher
{
    private static readonly byte[] EmptyHash = SHA256.HashData(Array.Empty<byte>());

    public static byte[] Hash(IPAddress? ip)
    {
        if (ip is null) return EmptyHash;
        return SHA256.HashData(Encoding.UTF8.GetBytes(ip.ToString()));
    }

    public static byte[]? HashFromHeader(string? hexHash)
    {
        if (string.IsNullOrEmpty(hexHash)) return null;
        try { return Convert.FromHexString(hexHash); } catch { return null; }
    }
}
