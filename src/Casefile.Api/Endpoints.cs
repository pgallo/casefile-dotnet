using Casefile.Core;
using Casefile.Core.Domain;
using Casefile.Core.Entities;
using Casefile.Core.Persistence;
using Casefile.Search;

namespace Casefile.Api;

public static class Endpoints
{
    private static readonly string[] DefaultFacets = [CaseIndexMapper.Fields.Status, CaseIndexMapper.Fields.Tag, CaseIndexMapper.Fields.EntityType];

    /// <summary>Who is acting. A real deployment would take this from an authenticated principal.</summary>
    private static string Actor(HttpContext ctx) =>
        ctx.Request.Headers.TryGetValue("X-Actor", out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : "anonymous";

    public static void MapCasefileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", (CaseService s) => Results.Ok(new { status = "ok", cases = s.Count }));

        var cases = app.MapGroup("/cases");

        cases.MapGet("/", (CaseService s, CaseStatus? status, string? tag, int? limit) =>
            s.All()
                .Where(c => status is null || c.Status == status)
                .Where(c => tag is null || c.Tags.Contains(tag.ToLowerInvariant()))
                .Take(Math.Clamp(limit ?? 100, 1, 1000))
                .Select(CaseSummary.From));

        cases.MapGet("/{id}", (string id, CaseService s) => s.Get(id));

        cases.MapPost("/", (CreateCaseCommand cmd, CaseService s, HttpContext ctx) =>
        {
            var c = s.Create(cmd, Actor(ctx));
            return Results.Created($"/cases/{c.Id}", c);
        });

        cases.MapPatch("/{id}", (string id, UpdateCaseCommand cmd, CaseService s, HttpContext ctx) => s.Update(id, cmd, Actor(ctx)));

        cases.MapDelete("/{id}", (string id, CaseService s, HttpContext ctx) =>
        {
            s.Delete(id, Actor(ctx));
            return Results.NoContent();
        });

        cases.MapPost("/{id}/documents", (string id, AddDocumentCommand cmd, CaseService s, HttpContext ctx) =>
        {
            var c = s.AddDocument(id, cmd, Actor(ctx));
            return Results.Created($"/cases/{id}/documents/{c.Documents[^1].Id}", c);
        });

        cases.MapDelete("/{id}/documents/{docId}", (string id, string docId, CaseService s, HttpContext ctx) =>
            s.RemoveDocument(id, docId, Actor(ctx)));

        cases.MapGet("/{id}/transitions", (string id, CaseService s) =>
        {
            var c = s.Get(id);
            return new TransitionsResponse(c.Status, Workflow.AllowedTargets(c.Status));
        });

        cases.MapPost("/{id}/transitions", (string id, TransitionCommand cmd, CaseService s, HttpContext ctx) =>
            s.Transition(id, cmd, Actor(ctx)));

        cases.MapGet("/{id}/audit", (string id, CaseService s) => s.Get(id).Audit);

        cases.MapGet("/{id}/related", (string id, int? limit, CaseService s) =>
        {
            var related = s.Related(id, Math.Clamp(limit ?? 10, 1, 100));
            var dtos = related.Select(r =>
            {
                var other = s.Get(r.CaseId);
                return new RelatedCaseDto(r.CaseId, other.Title, other.Status, r.Score, r.SharedEntities);
            }).ToList();
            return new RelatedResponse(id, dtos);
        });

        app.MapPost("/search", (SearchBody body, CaseService s) =>
        {
            var request = new SearchRequest
            {
                Query = body.Query,
                Filters = body.Filters?.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value),
                Facets = body.Facets ?? DefaultFacets,
                Limit = Math.Clamp(body.Limit ?? 10, 1, 100),
                Offset = Math.Max(0, body.Offset ?? 0),
            };
            var r = s.Search(request);
            var hits = r.Hits.Select(h => new SearchHitDto(h.Case.Id, h.Case.Title, h.Case.Status, h.Case.Tags, h.Score, h.Snippets)).ToList();
            return new SearchResponse(r.Total, r.ParsedQuery, hits, r.Facets);
        });

        app.MapGet("/entities/{type}/{key}/cases", (EntityType type, string key, CaseService s) =>
            new EntityCasesResponse(type, key, s.CasesForEntity(type, key)));

        app.MapPost("/extract", (ExtractBody body, CaseService s) => s.ExtractPreview(body.Text ?? ""));

        app.MapPost("/admin/snapshot", async (CasefileOptions options, CaseService s) =>
        {
            if (options.SnapshotPath is null) return Results.Problem("No snapshot path configured (Casefile:SnapshotPath).", statusCode: 400);
            await s.SaveAsync(new SnapshotStore(options.SnapshotPath));
            return Results.Ok(new { saved = s.Count, path = options.SnapshotPath });
        });
    }
}
