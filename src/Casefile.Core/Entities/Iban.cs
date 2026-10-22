using System.Numerics;
using System.Text;

namespace Casefile.Core.Entities;

/// <summary>ISO 13616 style mod-97 checksum, used to reject random alphanumeric runs that only look like account numbers.</summary>
public static class Iban
{
    public static string Normalize(string raw) => raw.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    public static bool IsValid(string raw)
    {
        var s = Normalize(raw);
        if (s.Length is < 15 or > 34) return false;
        if (!char.IsAsciiLetterUpper(s[0]) || !char.IsAsciiLetterUpper(s[1]) || !char.IsAsciiDigit(s[2]) || !char.IsAsciiDigit(s[3])) return false;
        if (!s.All(char.IsAsciiLetterOrDigit)) return false;
        return Mod97(s[4..] + s[..4]) == 1;
    }

    /// <summary>Builds a checksum-valid number from a country code and BBAN (used by the synthetic seed).</summary>
    public static string Create(string country, string bban)
    {
        var check = 98 - Mod97(bban + country + "00");
        return $"{country}{check:D2}{bban}";
    }

    /// <summary>Groups of four, the way people write them in reports.</summary>
    public static string Pretty(string iban)
    {
        var s = Normalize(iban);
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static int Mod97(string s)
    {
        var digits = new StringBuilder();
        foreach (var c in s) digits.Append(char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString());
        return (int)(BigInteger.Parse(digits.ToString()) % 97);
    }
}
