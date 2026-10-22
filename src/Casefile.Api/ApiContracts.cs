using Casefile.Core.Domain;
using Casefile.Core.Entities;
using Casefile.Core.Graph;
using Casefile.Search;

namespace Casefile.Api;

public sealed record SearchBody(
    string? Query,
    Dictionary<string, string[]>? Filters,
    string[]? Facets,
    int? Limit,
    int? Offset);

public sealed record CaseSummary(string Id, string Title, CaseStatus Status, Priority Priority, string? Assignee, IReadOnlyList<string> Tags, int Documents, DateTimeOffset UpdatedAt)
{
    public static CaseSummary From(Case c) => new(c.Id, c.Title, c.Status, c.Priority, c.Assignee, c.Tags, c.Documents.Count, c.UpdatedAt);
}

public sealed record SearchHitDto(string Id, string Title, CaseStatus Status, IReadOnlyList<string> Tags, double Score, IReadOnlyDictionary<string, string> Snippets);

public sealed record SearchResponse(int Total, string ParsedQuery, IReadOnlyList<SearchHitDto> Hits, IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> Facets);

public sealed record RelatedResponse(string CaseId, IReadOnlyList<RelatedCaseDto> Related);

public sealed record RelatedCaseDto(string Id, string Title, CaseStatus Status, double Score, IReadOnlyList<SharedEntity> SharedEntities);

public sealed record TransitionsResponse(CaseStatus Current, IReadOnlyList<CaseStatus> Allowed);

public sealed record ExtractBody(string Text);

public sealed record EntityCasesResponse(EntityType Type, string Key, IReadOnlyList<string> Cases);
