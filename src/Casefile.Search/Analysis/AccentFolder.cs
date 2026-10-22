using System.Globalization;
using System.Text;

namespace Casefile.Search.Analysis;

/// <summary>Removes diacritics so "società", "societa" and "SOCIETÀ" index to the same term.</summary>
public static class AccentFolder
{
    public static string Fold(string input)
    {
        var ascii = true;
        foreach (var c in input)
        {
            if (c > 127) { ascii = false; break; }
        }
        if (ascii) return input;

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            switch (c)
            {
                case 'ß': sb.Append("ss"); break;
                case 'æ': sb.Append("ae"); break;
                case 'Æ': sb.Append("AE"); break;
                case 'ø': sb.Append('o'); break;
                case 'Ø': sb.Append('O'); break;
                case 'ł': sb.Append('l'); break;
                case 'Ł': sb.Append('L'); break;
                case 'đ': sb.Append('d'); break;
                default:
                    if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
                    break;
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
