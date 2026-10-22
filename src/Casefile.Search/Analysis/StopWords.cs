namespace Casefile.Search.Analysis;

/// <summary>Stopword lists, stored in folded lowercase form because they are applied after accent folding.</summary>
public static class StopWords
{
    public static readonly IReadOnlySet<string> English = new HashSet<string>(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "been", "but", "by", "for", "from", "had", "has", "have",
        "he", "her", "his", "if", "in", "into", "is", "it", "its", "no", "not", "of", "on", "or", "our",
        "she", "so", "such", "that", "the", "their", "then", "there", "these", "they", "this", "to", "was",
        "we", "were", "which", "while", "who", "will", "with", "would", "you", "your",
    };

    public static readonly IReadOnlySet<string> Italian = new HashSet<string>(StringComparer.Ordinal)
    {
        "a", "ad", "al", "alla", "alle", "agli", "ai", "anche", "che", "chi", "ci", "come", "con", "da", "dal",
        "dalla", "dalle", "dai", "degli", "dei", "del", "della", "delle", "dell", "di", "e", "ed", "gli", "ha",
        "hanno", "i", "il", "in", "l", "la", "le", "lo", "ma", "mi", "nei", "nel", "nella", "nelle", "non",
        "o", "per", "perche", "piu", "quale", "quando", "questa", "questo", "se", "si", "sono", "sta", "stato",
        "stata", "su", "sul", "sulla", "tra", "fra", "un", "una", "uno", "all", "dall", "nell", "sull",
    };

    public static readonly IReadOnlySet<string> EnglishAndItalian =
        new HashSet<string>(English.Concat(Italian), StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> None = new HashSet<string>();
}
