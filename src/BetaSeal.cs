using System.Security.Cryptography;
using System.Text;

namespace Obozny;

/// <summary>
/// HMAC-SHA256 прийняття бети. Зберігається лише в obozny.json (Settings.BetaSeal).
/// Без прив'язки до машини / MachineGuid / fingerprint.
/// </summary>
internal static class BetaSeal
{
    private static readonly byte[] KeySeed =
    [
        0x4F, 0x62, 0x6F, 0x7A, 0x6E, 0x79, 0x42, 0x65, 0x74, 0x61, 0x53, 0x65, 0x61, 0x6C, 0x21, 0x39,
        0xD4, 0x1A, 0x7C, 0xE2, 0x55, 0x90, 0x3B, 0xF1, 0x88, 0x2D, 0x6E, 0xA4, 0xC0, 0x17, 0xBE, 0x63
    ];

    public static string Compute(DateTime acceptedAt)
    {
        var payload = $"v1|{acceptedAt.Date:yyyy-MM-dd}|accepted";
        using var hmac = new HMACSHA256(KeySeed);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    public static bool Verify(DateTime acceptedAt, string? seal)
    {
        if (string.IsNullOrWhiteSpace(seal)) return false;
        try
        {
            var expected = Compute(acceptedAt.Date);
            var a = Encoding.ASCII.GetBytes(expected);
            var b = Encoding.ASCII.GetBytes(seal.Trim().ToUpperInvariant());
            if (a.Length != b.Length) return false;
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
        catch
        {
            return false;
        }
    }
}
