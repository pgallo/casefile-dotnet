namespace Casefile.Search.Analysis;

/// <summary>
/// A deliberately light, language-agnostic stemmer for mixed English and Italian text.
/// It strips English plural and -ing/-ed suffixes, then the final Italian gender/number vowel on
/// longer words. It over-stems some English words ("invoice" becomes "invoic"), which is harmless
/// because the same function runs on both indexed text and queries; what matters is that
/// "invoices", "invoice", "bonifico" and "bonifici" collapse consistently.
/// </summary>
public static class LightStemmer
{
    public static string Stem(string word)
    {
        if (word.Length <= 3 || !char.IsLetter(word[^1])) return word;
        var w = word;

        if (w.EndsWith("ies", StringComparison.Ordinal) && w.Length > 4) w = w[..^3] + "y";
        else if (w.EndsWith("sses", StringComparison.Ordinal)) w = w[..^2];
        else if (w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal)
                 && !w.EndsWith("us", StringComparison.Ordinal) && !w.EndsWith("is", StringComparison.Ordinal))
            w = w[..^1];

        if (w.EndsWith("ing", StringComparison.Ordinal) && w.Length > 5) w = Undouble(w[..^3]);
        else if (w.EndsWith("ed", StringComparison.Ordinal) && w.Length > 4) w = Undouble(w[..^2]);

        if (w.Length > 5 && w[^1] is 'a' or 'e' or 'i' or 'o')
        {
            w = w[..^1];
            // Italian "-chi"/"-ghi" plurals: "bianchi" -> "bianch" -> "bianc", matching "bianco" -> "bianc".
            if (w.EndsWith("ch", StringComparison.Ordinal) || w.EndsWith("gh", StringComparison.Ordinal)) w = w[..^1];
        }
        return w;
    }

    private static string Undouble(string w) =>
        w.Length >= 2 && w[^1] == w[^2] && w[^1] is not ('l' or 's' or 'z') && !"aeiou".Contains(w[^1]) ? w[..^1] : w;
}
