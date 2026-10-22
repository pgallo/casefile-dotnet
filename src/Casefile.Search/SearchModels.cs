namespace Casefile.Search;

public sealed record SearchRequest
{
    public string? Query { get; init; }

    /// <summary>Keyword filters: values within a field are OR-ed, fields are AND-ed.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Filters { get; init; }

    /// <summary>Keyword fields to compute facet counts for.</summary>
    public IReadOnlyList<string>? Facets { get; init; }

    public int Limit { get; init; } = 10;
    public int Offset { get; init; }

    /// <summary>Text fields to build snippets from, in order of preference.</summary>
    public IReadOnlyList<string>? HighlightFields { get; init; }
}

public sealed record SearchHit(string Id, double Score, IReadOnlyDictionary<string, string> Snippets);

public sealed record FacetValue(string Value, int Count);

public sealed record SearchResults(
    int Total,
    IReadOnlyList<SearchHit> Hits,
    IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> Facets,
    string ParsedQuery);
