using Casefile.Core.Entities;

namespace Casefile.Core.Graph;

public sealed record SharedEntity(EntityType Type, string Key, string Display, double Weight, int CaseFrequency);

public sealed record RelatedCase(string CaseId, double Score, IReadOnlyList<SharedEntity> SharedEntities);

/// <summary>
/// Bipartite graph between cases and entities, kept as two adjacency maps. Related cases are found by a
/// two-hop walk (case -> entity -> case). Each shared entity contributes an IDF-like weight
/// ln(1 + N / df), times a per-type factor, so a phone number seen in two cases links them far more
/// strongly than a city that appears in half the corpus. Entities above a document-frequency ceiling
/// are treated as hubs and ignored, which bounds the walk cost and removes noise links.
/// </summary>
public sealed class LinkGraph
{
    private readonly Dictionary<string, HashSet<string>> _caseToEntities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _entityToCases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExtractedEntity> _entityInfo = new(StringComparer.Ordinal);
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>Hard identifiers say more about a link than a shared place name.</summary>
    public static double TypeFactor(EntityType t) => t switch
    {
        EntityType.Iban => 1.5,
        EntityType.Phone or EntityType.Email => 1.3,
        EntityType.Person or EntityType.Organisation => 1.0,
        EntityType.Location => 0.4,
        _ => 1.0,
    };

    public double HubFraction { get; init; } = 0.25;

    public void SetCase(string caseId, IEnumerable<ExtractedEntity> entities)
    {
        _lock.EnterWriteLock();
        try
        {
            RemoveUnlocked(caseId);
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in entities)
            {
                set.Add(e.NodeId);
                if (!_entityToCases.TryGetValue(e.NodeId, out var cases)) _entityToCases[e.NodeId] = cases = new(StringComparer.Ordinal);
                cases.Add(caseId);
                _entityInfo.TryAdd(e.NodeId, e);
            }
            _caseToEntities[caseId] = set;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void RemoveCase(string caseId)
    {
        _lock.EnterWriteLock();
        try { RemoveUnlocked(caseId); }
        finally { _lock.ExitWriteLock(); }
    }

    public int CaseCount
    {
        get { _lock.EnterReadLock(); try { return _caseToEntities.Count; } finally { _lock.ExitReadLock(); } }
    }

    public IReadOnlyList<RelatedCase> Related(string caseId, int limit = 10, double minScore = 0)
    {
        _lock.EnterReadLock();
        try
        {
            if (!_caseToEntities.TryGetValue(caseId, out var mine)) return [];
            var n = _caseToEntities.Count;
            var hubCeiling = Math.Max(2, (int)Math.Ceiling(n * HubFraction));
            var acc = new Dictionary<string, (double Score, List<SharedEntity> Shared)>(StringComparer.Ordinal);

            foreach (var node in mine)
            {
                var cases = _entityToCases[node];
                if (cases.Count < 2 || cases.Count > hubCeiling) continue;
                var info = _entityInfo[node];
                var weight = Math.Log(1.0 + (double)n / cases.Count) * TypeFactor(info.Type);
                var shared = new SharedEntity(info.Type, info.Key, info.Display, Math.Round(weight, 4), cases.Count);
                foreach (var other in cases)
                {
                    if (other == caseId) continue;
                    if (!acc.TryGetValue(other, out var entry)) entry = (0, []);
                    entry.Shared.Add(shared);
                    acc[other] = (entry.Score + weight, entry.Shared);
                }
            }

            return acc
                .Where(kv => kv.Value.Score > minScore)
                .OrderByDescending(kv => kv.Value.Score).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(limit)
                .Select(kv => new RelatedCase(kv.Key, Math.Round(kv.Value.Score, 4),
                    kv.Value.Shared.OrderByDescending(s => s.Weight).ThenBy(s => s.Key, StringComparer.Ordinal).ToList()))
                .ToList();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Cases mentioning an entity, for "who else has seen this phone number" lookups.</summary>
    public IReadOnlyList<string> CasesFor(EntityType type, string key)
    {
        _lock.EnterReadLock();
        try
        {
            return _entityToCases.TryGetValue($"{type}:{key}", out var cases)
                ? cases.OrderBy(c => c, StringComparer.Ordinal).ToList()
                : [];
        }
        finally { _lock.ExitReadLock(); }
    }

    private void RemoveUnlocked(string caseId)
    {
        if (!_caseToEntities.Remove(caseId, out var nodes)) return;
        foreach (var node in nodes)
        {
            var cases = _entityToCases[node];
            cases.Remove(caseId);
            if (cases.Count == 0)
            {
                _entityToCases.Remove(node);
                _entityInfo.Remove(node);
            }
        }
    }
}
