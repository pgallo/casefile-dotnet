using Casefile.Search.Analysis;
using Casefile.Search.Indexing;
using Casefile.Search.Querying;

namespace Casefile.Search;

/// <summary>
/// Thread-safe facade over the inverted index. Many concurrent searches, one writer at a time.
/// Writes are incremental (upsert/remove of a single document), so an edit in the API is visible to
/// the next search without a rebuild.
/// </summary>
public sealed class SearchIndex : IDisposable
{
    private readonly InvertedIndex _index;
    private readonly Bm25 _bm25;
    private readonly Highlighter _highlighter;
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    public SearchIndex(IndexSchema schema, Analyzer? analyzer = null, Bm25? bm25 = null)
    {
        var a = analyzer ?? Analyzer.Default;
        _index = new InvertedIndex(schema, a);
        _bm25 = bm25 ?? new Bm25();
        _highlighter = new Highlighter(a);
    }

    public IndexSchema Schema => _index.Schema;

    public int Count
    {
        get { _lock.EnterReadLock(); try { return _index.DocCount; } finally { _lock.ExitReadLock(); } }
    }

    public void Upsert(IndexDocument doc)
    {
        _lock.EnterWriteLock();
        try { _index.Upsert(doc); }
        finally { _lock.ExitWriteLock(); }
    }

    public bool Remove(string id)
    {
        _lock.EnterWriteLock();
        try { return _index.Remove(id); }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Replaces the whole content atomically (used after loading a snapshot).</summary>
    public void Rebuild(IEnumerable<IndexDocument> docs)
    {
        var list = docs.ToList();
        _lock.EnterWriteLock();
        try
        {
            foreach (var id in _index.AllDocs.Select(_index.ExternalId).ToList()) _index.Remove(id);
            foreach (var d in list) _index.Upsert(d);
        }
        finally { _lock.ExitWriteLock(); }
    }

    public SearchResults Search(SearchRequest request)
    {
        var ast = QueryParser.Parse(request.Query);
        _lock.EnterReadLock();
        try
        {
            var executor = new QueryExecutor(_index, _bm25);
            var scored = executor.Execute(ast);

            var filters = NormalizeFilters(request.Filters);
            var matching = scored.Keys.Where(d => PassesFilters(d, filters, skipField: null)).ToList();

            var facets = new Dictionary<string, IReadOnlyList<FacetValue>>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawField in request.Facets ?? [])
            {
                if (!_index.Schema.IsKeyword(rawField)) throw new UnknownFieldException(rawField);
                var field = _index.Schema.Canonical(rawField);
                // Multi-select faceting: counts for a field ignore that field's own filter, so the UI can
                // show how many results each alternative value would give.
                var docs = scored.Keys.Where(d => PassesFilters(d, filters, skipField: field));
                facets[field] = CountFacet(field, docs);
            }

            var terms = executor.PositiveTerms(ast);
            var highlightFields = request.HighlightFields ?? _index.Schema.TextFields.ToList();
            var hits = matching
                .OrderByDescending(d => scored[d])
                .ThenBy(d => _index.ExternalId(d), StringComparer.Ordinal)
                .Skip(Math.Max(0, request.Offset))
                .Take(Math.Clamp(request.Limit, 0, 1000))
                .Select(d => new SearchHit(_index.ExternalId(d), Math.Round(scored[d], 4), Snippets(d, terms, highlightFields)))
                .ToList();

            return new SearchResults(matching.Count, hits, facets, ast.ToString());
        }
        finally { _lock.ExitReadLock(); }
    }

    private Dictionary<string, HashSet<string>> NormalizeFilters(IReadOnlyDictionary<string, IReadOnlyList<string>>? filters)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (filters is null) return result;
        foreach (var (field, values) in filters)
        {
            if (!_index.Schema.IsKeyword(field)) throw new UnknownFieldException(field);
            if (values.Count == 0) continue;
            result[_index.Schema.Canonical(field)] = values.Select(Analyzer.NormalizeKeyword).ToHashSet(StringComparer.Ordinal);
        }
        return result;
    }

    private bool PassesFilters(int doc, Dictionary<string, HashSet<string>> filters, string? skipField)
    {
        var stored = _index.Stored(doc);
        foreach (var (field, wanted) in filters)
        {
            if (skipField is not null && field.Equals(skipField, StringComparison.OrdinalIgnoreCase)) continue;
            if (!stored.Keywords.TryGetValue(field, out var have) || !have.Overlaps(wanted)) return false;
        }
        return true;
    }

    private List<FacetValue> CountFacet(string field, IEnumerable<int> docs)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var d in docs)
            if (_index.Stored(d).Keywords.TryGetValue(field, out var values))
                foreach (var v in values) counts[v] = counts.GetValueOrDefault(v) + 1;
        return counts
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FacetValue(_index.KeywordDisplay(field, kv.Key), kv.Value))
            .ToList();
    }

    private Dictionary<string, string> Snippets(int doc, HashSet<string> terms, IEnumerable<string> fields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (terms.Count == 0) return result;
        var stored = _index.Stored(doc);
        foreach (var field in fields)
        {
            if (!stored.Text.TryGetValue(field, out var values)) continue;
            foreach (var value in values)
            {
                var snippet = _highlighter.Snippet(value, terms);
                if (snippet is null) continue;
                result[_index.Schema.Canonical(field)] = snippet;
                break;
            }
        }
        return result;
    }

    public void Dispose() => _lock.Dispose();
}
