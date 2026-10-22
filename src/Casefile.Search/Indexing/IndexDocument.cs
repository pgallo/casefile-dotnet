namespace Casefile.Search.Indexing;

/// <summary>
/// A document as the index sees it. Text fields are analyzed and scored; keyword fields are exact-match
/// values used for filters and facets. A text field may hold several values (e.g. one per attached note):
/// they are indexed with a position gap so phrase queries never match across value boundaries.
/// </summary>
public sealed class IndexDocument
{
    public IndexDocument(string id) => Id = id;

    public string Id { get; }
    public Dictionary<string, List<string>> Text { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> Keywords { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IndexDocument AddText(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        if (!Text.TryGetValue(field, out var list)) Text[field] = list = [];
        list.Add(value);
        return this;
    }

    public IndexDocument AddKeyword(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        if (!Keywords.TryGetValue(field, out var list)) Keywords[field] = list = [];
        list.Add(value);
        return this;
    }
}
