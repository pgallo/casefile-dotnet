using System.Collections.Immutable;
using Casefile.Core.Domain;
using Casefile.Core.Entities;
using Casefile.Core.Graph;
using Casefile.Core.Persistence;
using Casefile.Search;

namespace Casefile.Core;

public sealed record CaseSearchHit(Case Case, double Score, IReadOnlyDictionary<string, string> Snippets);

public sealed record CaseSearchResults(
    int Total,
    IReadOnlyList<CaseSearchHit> Hits,
    IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> Facets,
    string ParsedQuery);

/// <summary>
/// Application service: owns the case store and keeps the derived structures (search index, link graph)
/// in step with it. Writers are serialized by one lock; every write replaces an immutable <see cref="Case"/>
/// and then updates the index and graph for that single case. Readers never take this lock: they read the
/// current immutable dictionary, and the index and graph have their own reader/writer locks.
/// </summary>
public sealed class CaseService : IDisposable
{
    private readonly object _writeLock = new();
    private ImmutableSortedDictionary<string, Case> _cases = ImmutableSortedDictionary.Create<string, Case>(StringComparer.Ordinal);
    private readonly SearchIndex _index = new(CaseIndexMapper.Schema);
    private readonly LinkGraph _graph = new();
    private readonly EntityExtractor _extractor;
    private readonly TimeProvider _clock;
    private int _nextCaseNumber = 1;
    private int _nextDocNumber = 1;

    public CaseService(EntityExtractor extractor, TimeProvider? clock = null)
    {
        _extractor = extractor;
        _clock = clock ?? TimeProvider.System;
    }

    public int Count => _cases.Count;

    public IReadOnlyList<Case> All() => _cases.Values.ToList();

    public Case Get(string id) => _cases.TryGetValue(id, out var c) ? c : throw new NotFoundException($"Case {id}");

    public Case Create(CreateCaseCommand cmd, string actor)
    {
        if (string.IsNullOrWhiteSpace(cmd.Title)) throw new ValidationException("Title is required.");
        lock (_writeLock)
        {
            var now = _clock.GetUtcNow();
            var c = new Case
            {
                Id = $"CF-{_nextCaseNumber++:D4}",
                Title = cmd.Title.Trim(),
                Summary = cmd.Summary?.Trim() ?? "",
                Priority = cmd.Priority,
                Assignee = NullIfBlank(cmd.Assignee),
                Tags = NormalizeTags(cmd.Tags),
                CreatedAt = now,
                UpdatedAt = now,
                Audit = [new AuditEntry(now, actor, "created", cmd.Title.Trim())],
            };
            return Commit(WithEntities(c));
        }
    }

    public Case Update(string id, UpdateCaseCommand cmd, string actor)
    {
        lock (_writeLock)
        {
            var c = Get(id);
            if (cmd.ExpectedVersion is { } v && v != c.Version) throw new ConcurrencyException(id, v, c.Version);
            Workflow.EnsureEditable(c);
            if (cmd.Title is not null && string.IsNullOrWhiteSpace(cmd.Title)) throw new ValidationException("Title cannot be blank.");

            var changes = new List<string>();
            var updated = c;
            if (cmd.Title is not null && cmd.Title.Trim() != c.Title) { updated = updated with { Title = cmd.Title.Trim() }; changes.Add("title"); }
            if (cmd.Summary is not null && cmd.Summary.Trim() != c.Summary) { updated = updated with { Summary = cmd.Summary.Trim() }; changes.Add("summary"); }
            if (cmd.Priority is { } p && p != c.Priority) { updated = updated with { Priority = p }; changes.Add($"priority={p}"); }
            if (cmd.Assignee is not null && NullIfBlank(cmd.Assignee) != c.Assignee) { updated = updated with { Assignee = NullIfBlank(cmd.Assignee) }; changes.Add($"assignee={NullIfBlank(cmd.Assignee) ?? "none"}"); }
            if (cmd.Tags is not null)
            {
                var tags = NormalizeTags(cmd.Tags);
                if (!tags.SequenceEqual(c.Tags)) { updated = updated with { Tags = tags }; changes.Add($"tags={string.Join(',', tags)}"); }
            }
            if (changes.Count == 0) return c;
            return Commit(Touch(WithEntities(updated), actor, "updated", string.Join("; ", changes)));
        }
    }

    public void Delete(string id, string actor)
    {
        lock (_writeLock)
        {
            var c = Get(id);
            if (c.Status != CaseStatus.Open)
                throw new WorkflowViolationException("delete-only-open", $"Only Open cases can be deleted; {id} is {c.Status}.");
            _cases = _cases.Remove(id);
            _index.Remove(id);
            _graph.RemoveCase(id);
        }
    }

    public Case AddDocument(string caseId, AddDocumentCommand cmd, string actor)
    {
        if (string.IsNullOrWhiteSpace(cmd.Title)) throw new ValidationException("Document title is required.");
        lock (_writeLock)
        {
            var c = Get(caseId);
            Workflow.EnsureEditable(c);
            var doc = new CaseDocument
            {
                Id = $"D-{_nextDocNumber++:D5}",
                Kind = cmd.Kind,
                Title = cmd.Title.Trim(),
                Body = cmd.Body ?? "",
                Author = actor,
                CreatedAt = _clock.GetUtcNow(),
            };
            var updated = c with { Documents = c.Documents.Add(doc) };
            return Commit(Touch(WithEntities(updated), actor, "document-added", $"{doc.Id} {doc.Kind}: {doc.Title}"));
        }
    }

    public Case RemoveDocument(string caseId, string documentId, string actor)
    {
        lock (_writeLock)
        {
            var c = Get(caseId);
            Workflow.EnsureEditable(c);
            var doc = c.Documents.FirstOrDefault(d => d.Id == documentId) ?? throw new NotFoundException($"Document {documentId}");
            var updated = c with { Documents = c.Documents.Remove(doc) };
            return Commit(Touch(WithEntities(updated), actor, "document-removed", doc.Id));
        }
    }

    public Case Transition(string caseId, TransitionCommand cmd, string actor)
    {
        lock (_writeLock)
        {
            var c = Get(caseId);
            var rule = Workflow.EnsureCanTransition(c, cmd.To, actor, cmd.Reason);
            var detail = $"{c.Status} -> {cmd.To} ({rule})" + (string.IsNullOrWhiteSpace(cmd.Reason) ? "" : $": {cmd.Reason.Trim()}");
            return Commit(Touch(c with { Status = cmd.To }, actor, "status-changed", detail));
        }
    }

    public CaseSearchResults Search(SearchRequest request)
    {
        var r = _index.Search(request);
        var cases = _cases;
        var hits = r.Hits
            .Where(h => cases.ContainsKey(h.Id))
            .Select(h => new CaseSearchHit(cases[h.Id], h.Score, h.Snippets))
            .ToList();
        return new CaseSearchResults(r.Total, hits, r.Facets, r.ParsedQuery);
    }

    public IReadOnlyList<RelatedCase> Related(string caseId, int limit = 10)
    {
        _ = Get(caseId);
        return _graph.Related(caseId, limit);
    }

    public IReadOnlyList<string> CasesForEntity(EntityType type, string key) => _graph.CasesFor(type, key);

    public IReadOnlyList<EntityMention> ExtractPreview(string text) => _extractor.ExtractMentions(text, "preview");

    public Task SaveAsync(SnapshotStore store, CancellationToken ct = default) => store.SaveAsync(All(), ct);

    /// <summary>Replaces all state with the snapshot and rebuilds index and graph from scratch.</summary>
    public async Task LoadAsync(SnapshotStore store, CancellationToken ct = default)
    {
        var loaded = await store.LoadAsync(ct);
        ReplaceAll(loaded, reextract: false);
    }

    /// <summary>Bulk import used by load and seeding. Re-extraction is optional so a snapshot keeps exactly the entities it was saved with.</summary>
    public void ReplaceAll(IEnumerable<Case> cases, bool reextract)
    {
        lock (_writeLock)
        {
            var list = cases.Select(c => reextract ? WithEntities(c) : c).ToList();
            _cases = list.ToImmutableSortedDictionary(c => c.Id, c => c, StringComparer.Ordinal);
            _index.Rebuild(list.Select(CaseIndexMapper.ToIndexDocument));
            foreach (var id in _cases.Keys) _graph.RemoveCase(id);
            foreach (var c in list) _graph.SetCase(c.Id, c.Entities);
            _nextCaseNumber = NextNumber(list.Select(c => c.Id), "CF-");
            _nextDocNumber = NextNumber(list.SelectMany(c => c.Documents).Select(d => d.Id), "D-");
        }
    }

    private Case Commit(Case c)
    {
        _cases = _cases.SetItem(c.Id, c);
        _index.Upsert(CaseIndexMapper.ToIndexDocument(c));
        _graph.SetCase(c.Id, c.Entities);
        return c;
    }

    private Case Touch(Case c, string actor, string action, string detail)
    {
        var now = _clock.GetUtcNow();
        return c with
        {
            UpdatedAt = now,
            Version = c.Version + 1,
            Audit = c.Audit.Add(new AuditEntry(now, actor, action, detail)),
        };
    }

    private Case WithEntities(Case c)
    {
        var texts = new List<(string, string)> { ("title", c.Title), ("summary", c.Summary) };
        foreach (var d in c.Documents) { texts.Add((d.Id, d.Title)); texts.Add((d.Id, d.Body)); }
        return c with { Entities = [.. _extractor.Extract(texts)] };
    }

    private static ImmutableList<string> NormalizeTags(IReadOnlyList<string>? tags) =>
        (tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().Order(StringComparer.Ordinal).ToImmutableList();

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static int NextNumber(IEnumerable<string> ids, string prefix) =>
        ids.Select(id => id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), out var n) ? n : 0)
           .DefaultIfEmpty(0).Max() + 1;

    public void Dispose() => _index.Dispose();
}
