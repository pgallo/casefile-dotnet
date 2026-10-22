namespace Casefile.Search.Indexing;

/// <summary>Declares the text fields (with BM25 boosts) and keyword fields the index accepts.</summary>
public sealed class IndexSchema
{
    private readonly Dictionary<string, double> _textFields;
    private readonly HashSet<string> _keywordFields;

    public IndexSchema(IReadOnlyDictionary<string, double> textFields, IEnumerable<string> keywordFields)
    {
        _textFields = new Dictionary<string, double>(textFields, StringComparer.OrdinalIgnoreCase);
        _keywordFields = new HashSet<string>(keywordFields, StringComparer.OrdinalIgnoreCase);
        var overlap = _textFields.Keys.Where(_keywordFields.Contains).ToList();
        if (overlap.Count > 0) throw new ArgumentException($"Fields declared as both text and keyword: {string.Join(", ", overlap)}");
    }

    public IReadOnlyCollection<string> TextFields => _textFields.Keys;
    public IReadOnlyCollection<string> KeywordFields => _keywordFields;
    public bool IsText(string field) => _textFields.ContainsKey(field);
    public bool IsKeyword(string field) => _keywordFields.Contains(field);
    public double Boost(string field) => _textFields.TryGetValue(field, out var b) ? b : 1.0;

    /// <summary>Canonical (declared) spelling of a field name, used as dictionary key internally.</summary>
    public string Canonical(string field) =>
        _textFields.Keys.FirstOrDefault(f => f.Equals(field, StringComparison.OrdinalIgnoreCase))
        ?? _keywordFields.FirstOrDefault(f => f.Equals(field, StringComparison.OrdinalIgnoreCase))
        ?? field;
}
