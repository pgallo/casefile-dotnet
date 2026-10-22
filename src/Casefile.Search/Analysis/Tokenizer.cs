using System.Globalization;

namespace Casefile.Search.Analysis;

/// <summary>
/// Splits text into runs of letters, digits and combining marks. Everything else is a separator,
/// including apostrophes, which handles Italian elision ("dell'ufficio" becomes "dell", "ufficio").
/// </summary>
public static class Tokenizer
{
    public static IEnumerable<Token> Tokenize(string text)
    {
        var position = 0;
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && !IsWordChar(text[i])) i++;
            if (i >= text.Length) yield break;
            var start = i;
            while (i < text.Length && IsWordChar(text[i])) i++;
            yield return new Token(text[start..i], position++, start, i);
        }
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;
}
