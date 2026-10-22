using Casefile.Search.Analysis;

namespace Casefile.Search.Indexing;

/// <summary>
/// In-memory inverted index with positional postings, per-field length statistics and keyword
/// (exact value) postings. Not thread-safe on its own: <see cref="SearchIndex"/> guards it with a
/// reader/writer lock. Internal document numbers are dense ints; external ids are strings.
/// </summary>
internal sealed class InvertedIndex(IndexSchema schema, Analyzer analyzer)
{
    /// <summary>Gap inserted between values of a multi-valued text field so phrases cannot straddle them.</summary>
    public const int PositionGap = 100;

    // field -> term -> doc -> sorted positions
    private readonly Dictionary<string, Dictionary<string, Dictionary<int, List<int>>>> _postings = new(StringComparer.OrdinalIgnoreCase);
    // field -> doc -> token count
    private readonly Dictionary<string, Dictionary<int, int>> _fieldLengths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _totalFieldLength = new(StringComparer.OrdinalIgnoreCase);
    // keyword field -> normalized value -> docs
    private readonly Dictionary<string, Dictionary<string, HashSet<int>>> _keywords = new(StringComparer.OrdinalIgnoreCase);
    // keyword field -> normalized value -> display value (first seen spelling)
    private readonly Dictionary<string, Dictionary<string, string>> _keywordDisplay = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, int> _docNumbers = new(StringComparer.Ordinal);
    private readonly Dictionary<int, StoredDocument> _stored = [];
    private int _nextDoc;

    public IndexSchema Schema => schema;
    public Analyzer Analyzer => analyzer;
    public int DocCount => _stored.Count;
    public IEnumerable<int> AllDocs => _stored.Keys;

    public void Upsert(IndexDocument doc)
    {
        Remove(doc.Id);
        var n = _nextDoc++;
        _docNumbers[doc.Id] = n;
        var stored = new StoredDocument(doc.Id);

        foreach (var (rawField, values) in doc.Text)
        {
            if (!schema.IsText(rawField)) throw new ArgumentException($"'{rawField}' is not a text field in the schema.");
            var field = schema.Canonical(rawField);
            var termsInField = new HashSet<string>(StringComparer.Ordinal);
            var basePosition = 0;
            var length = 0;
            foreach (var value in values)
            {
                var tokens = analyzer.Analyze(value);
                foreach (var t in tokens)
                {
                    AddPosting(field, t.Term, n, basePosition + t.Position);
                    termsInField.Add(t.Term);
                }
                length += tokens.Count;
                var lastPos = tokens.Count > 0 ? tokens[^1].Position : 0;
                basePosition += lastPos + 1 + PositionGap;
            }
            stored.Text[field] = [.. values];
            stored.Terms[field] = termsInField;
            if (!_fieldLengths.TryGetValue(field, out var lengths)) _fieldLengths[field] = lengths = [];
            lengths[n] = length;
            _totalFieldLength[field] = _totalFieldLength.GetValueOrDefault(field) + length;
        }

        foreach (var (rawField, values) in doc.Keywords)
        {
            if (!schema.IsKeyword(rawField)) throw new ArgumentException($"'{rawField}' is not a keyword field in the schema.");
            var field = schema.Canonical(rawField);
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                var key = Analyzer.NormalizeKeyword(value);
                if (!set.Add(key)) continue;
                if (!_keywords.TryGetValue(field, out var byValue)) _keywords[field] = byValue = new(StringComparer.Ordinal);
                if (!byValue.TryGetValue(key, out var docs)) byValue[key] = docs = [];
                docs.Add(n);
                if (!_keywordDisplay.TryGetValue(field, out var display)) _keywordDisplay[field] = display = new(StringComparer.Ordinal);
                display.TryAdd(key, value.Trim());
            }
            stored.Keywords[field] = set;
        }

        _stored[n] = stored;
    }

    public bool Remove(string id)
    {
        if (!_docNumbers.Remove(id, out var n)) return false;
        var stored = _stored[n];
        _stored.Remove(n);

        foreach (var (field, terms) in stored.Terms)
        {
            var byTerm = _postings[field];
            foreach (var term in terms)
            {
                var docs = byTerm[term];
                docs.Remove(n);
                if (docs.Count == 0) byTerm.Remove(term);
            }
            if (_fieldLengths[field].Remove(n, out var len)) _totalFieldLength[field] -= len;
        }
        foreach (var (field, values) in stored.Keywords)
        {
            var byValue = _keywords[field];
            foreach (var value in values)
            {
                var docs = byValue[value];
                docs.Remove(n);
                if (docs.Count == 0)
                {
                    byValue.Remove(value);
                    _keywordDisplay[field].Remove(value);
                }
            }
        }
        return true;
    }

    public string ExternalId(int doc) => _stored[doc].Id;
    public StoredDocument Stored(int doc) => _stored[doc];

    public IReadOnlyDictionary<int, List<int>> Postings(string field, string term) =>
        _postings.TryGetValue(field, out var byTerm) && byTerm.TryGetValue(term, out var docs)
            ? docs
            : EmptyPostings;

    public int DocFreq(string field, string term) => Postings(field, term).Count;

    public int FieldLength(string field, int doc) =>
        _fieldLengths.TryGetValue(field, out var l) ? l.GetValueOrDefault(doc) : 0;

    public double AvgFieldLength(string field)
    {
        if (!_fieldLengths.TryGetValue(field, out var l) || l.Count == 0) return 0;
        return (double)_totalFieldLength[field] / l.Count;
    }

    public IReadOnlySet<int> KeywordDocs(string field, string value)
    {
        var key = Analyzer.NormalizeKeyword(value);
        return _keywords.TryGetValue(field, out var byValue) && byValue.TryGetValue(key, out var docs)
            ? docs
            : EmptySet;
    }

    public string KeywordDisplay(string field, string normalizedValue) =>
        _keywordDisplay.TryGetValue(field, out var d) && d.TryGetValue(normalizedValue, out var v) ? v : normalizedValue;

    private void AddPosting(string field, string term, int doc, int position)
    {
        if (!_postings.TryGetValue(field, out var byTerm)) _postings[field] = byTerm = new(StringComparer.Ordinal);
        if (!byTerm.TryGetValue(term, out var docs)) byTerm[term] = docs = [];
        if (!docs.TryGetValue(doc, out var positions)) docs[doc] = positions = [];
        positions.Add(position); // positions arrive in increasing order per field
    }

    private static readonly Dictionary<int, List<int>> EmptyPostings = [];
    private static readonly HashSet<int> EmptySet = [];
}

internal sealed class StoredDocument(string id)
{
    public string Id { get; } = id;
    public Dictionary<string, List<string>> Text { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HashSet<string>> Terms { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HashSet<string>> Keywords { get; } = new(StringComparer.OrdinalIgnoreCase);
}
