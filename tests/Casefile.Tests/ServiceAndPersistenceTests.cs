using Casefile.Core;
using Casefile.Core.Domain;
using Casefile.Core.Entities;
using Casefile.Core.Persistence;
using Casefile.Core.Seed;
using Casefile.Search;

namespace Casefile.Tests;

public class ServiceAndPersistenceTests
{
    [Test]
    public void Edits_are_visible_to_search_and_graph_immediately()
    {
        using var s = TestCorpus.EmptyService();
        var a = TestCorpus.NewCase(s, "Alpha", "Call +44 7700 900417 about the shipment.");
        var b = TestCorpus.NewCase(s, "Beta", "Nothing relevant here.");
        Assert.Equal(0, s.Search(new SearchRequest { Query = "shipment" }).Hits.Count(h => h.Case.Id == b.Id));
        Assert.Equal(0, s.Related(a.Id).Count);

        s.AddDocument(b.Id, new("update", "Same caller +44 7700 900417 asked about the shipment."), "analyst.a");
        Assert.Equal(2, s.Search(new SearchRequest { Query = "shipment" }).Total);
        var related = s.Related(a.Id);
        Assert.Equal(b.Id, related.Single().CaseId);
        Assert.Equal(EntityType.Phone, related.Single().SharedEntities.Single().Type);

        s.Delete(b.Id, "analyst.a");
        Assert.Equal(1, s.Search(new SearchRequest { Query = "shipment" }).Total);
        Assert.Equal(0, s.Related(a.Id).Count);
    }

    [Test]
    public void Entity_facet_and_entity_filter_work_end_to_end()
    {
        using var s = TestCorpus.EmptyService();
        TestCorpus.NewCase(s, "One", "Mail a.ferrantelli@pellara.example re invoice.");
        TestCorpus.NewCase(s, "Two", "Invoice discussed in Napoli.");
        var r = s.Search(new SearchRequest
        {
            Query = "invoice",
            Facets = [CaseIndexMapper.Fields.EntityType],
            Filters = new Dictionary<string, IReadOnlyList<string>> { [CaseIndexMapper.Fields.Entity] = ["a.ferrantelli@pellara.example"] },
        });
        Assert.Equal("One", r.Hits.Single().Case.Title);
        Assert.Contains("Email", r.Facets[CaseIndexMapper.Fields.EntityType].Select(f => f.Value));
    }

    [Test]
    public async Task Snapshot_round_trip_rebuilds_index_and_graph()
    {
        var path = Path.Combine(Path.GetTempPath(), $"casefile-test-{Guid.NewGuid():N}.json");
        try
        {
            using var original = TestCorpus.EmptyService();
            Seeder.Seed(original);
            var store = new SnapshotStore(path);
            await original.SaveAsync(store);

            using var restored = TestCorpus.EmptyService();
            await restored.LoadAsync(store);
            Assert.Equal(original.Count, restored.Count);

            var q = new SearchRequest { Query = "wire transfer OR bonifici", Limit = 20 };
            Assert.Sequence(original.Search(q).Hits.Select(h => $"{h.Case.Id}:{h.Score}"), restored.Search(q).Hits.Select(h => $"{h.Case.Id}:{h.Score}"));
            Assert.Sequence(original.Related("CF-0001").Select(r => r.CaseId), restored.Related("CF-0001").Select(r => r.CaseId));
            Assert.Equal(original.Get("CF-0010").Audit.Count, restored.Get("CF-0010").Audit.Count);

            // Id counters continue after the highest loaded id.
            var next = restored.Create(new CreateCaseCommand("after load"), "analyst.a");
            Assert.Equal($"CF-{original.Count + 1:D4}", next.Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Seeded_corpus_has_expected_shape_and_is_deterministic()
    {
        using var a = TestCorpus.EmptyService();
        using var b = TestCorpus.EmptyService();
        Seeder.Seed(a);
        Seeder.Seed(b);
        Assert.True(a.Count is >= 35 and <= 45, $"about 40 cases, got {a.Count}");
        Assert.Sequence(a.All().Select(c => c.Title), b.All().Select(c => c.Title));
        foreach (CaseStatus st in Enum.GetValues<CaseStatus>()) Assert.True(a.All().Any(c => c.Status == st), $"has {st} cases");
        Assert.True(a.All().SelectMany(c => c.Entities).Any(e => e.Type == EntityType.Iban));
        Assert.True(a.Related("CF-0001").Count > 0, "seeded clusters produce links");
    }
}
