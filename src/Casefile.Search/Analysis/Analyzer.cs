namespace Casefile.Search.Analysis;

/// <summary>
/// Analyzer pipeline: tokenize, lowercase, fold accents, drop stopwords, stem.
/// Stopwords are dropped without renumbering positions, so a phrase query like "bank of genoa"
/// still requires a one-token gap between "bank" and "genoa".
/// </summary>
public sealed class Analyzer(IReadOnlySet<string> stopWords, bool stem = true)
{
    public static Analyzer Default { get; } = new(StopWords.EnglishAndItalian);

    public IReadOnlyList<Token> Analyze(string text)
    {
        var result = new List<Token>();
        foreach (var raw in Tokenizer.Tokenize(text))
        {
            var term = Normalize(raw.Term);
            if (stopWords.Contains(term)) continue;
            if (stem) term = LightStemmer.Stem(term);
            result.Add(raw with { Term = term });
        }
        return result;
    }

    /// <summary>Normalization used for keyword fields: no tokenizing, no stemming.</summary>
    public static string NormalizeKeyword(string value) => AccentFolder.Fold(value.Trim().ToLowerInvariant());

    private static string Normalize(string term) => AccentFolder.Fold(term.ToLowerInvariant());
}
