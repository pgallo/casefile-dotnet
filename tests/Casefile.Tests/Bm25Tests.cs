using Casefile.Search;
using Casefile.Search.Indexing;

namespace Casefile.Tests;

/// <summary>
/// Mini corpus, one text field, k1 = 1.2, b = 0.75. After analysis ("at" is a stopword, "reported" stems to "report"):
///   d1 "wire wire transfer"          len 3
///   d2 "wire payment"                len 2
///   d3 "cash deposit at branch"      len 3 (cash, deposit, branch)
///   d4 "wire transfer reported late" len 4
/// N = 4, avgdl = 12 / 4 = 3.
/// idf(wire)     = ln(1 + (4 - 3 + 0.5) / (3 + 0.5)) = ln(1.428571) = 0.356675
/// idf(transfer) = ln(1 + (4 - 2 + 0.5) / (2 + 0.5)) = ln(2)        = 0.693147
/// score = idf * tf * 2.2 / (tf + 1.2 * (0.25 + 0.75 * len / 3))
///   wire: d1 = idf * 4.4 / 3.2 = 0.490428, d2 = idf * 2.2 / 1.9 = 0.412992, d4 = idf * 2.2 / 2.5 = 0.313874
///   transfer: d1 = 0.693147, d4 = 0.693147 * 0.88 = 0.609970
/// </summary>
public class Bm25Tests : IDisposable
{
    private readonly SearchIndex _index;

    public Bm25Tests()
    {
        _index = new SearchIndex(new IndexSchema(new Dictionary<string, double> { ["body"] = 1.0 }, ["status"]));
        _index.Upsert(new IndexDocument("d1").AddText("body", "wire wire transfer").AddKeyword("status", "Open"));
        _index.Upsert(new IndexDocument("d2").AddText("body", "wire payment").AddKeyword("status", "Closed"));
        _index.Upsert(new IndexDocument("d3").AddText("body", "cash deposit at branch").AddKeyword("status", "Open"));
        _index.Upsert(new IndexDocument("d4").AddText("body", "wire transfer reported late").AddKeyword("status", "Open"));
    }

    public void Dispose() => _index.Dispose();

    private SearchResults Q(string q) => _index.Search(new SearchRequest { Query = q });

    [Test]
    public void Idf_matches_formula()
    {
        Assert.Near(0.356675, Bm25.Idf(4, 3), 1e-6);
        Assert.Near(Math.Log(2), Bm25.Idf(4, 2), 1e-9);
    }

    [Test]
    public void Single_term_scores_reflect_tf_saturation_and_length_normalization()
    {
        var r = Q("wire");
        Assert.Sequence(["d1", "d2", "d4"], r.Hits.Select(h => h.Id));
        Assert.Near(0.490428, r.Hits[0].Score);
        Assert.Near(0.412992, r.Hits[1].Score);
        Assert.Near(0.313874, r.Hits[2].Score);
    }

    [Test]
    public void Rare_term_reorders_multi_term_query()
    {
        // d4 is longer than d2 but contains the rarer "transfer", so it overtakes d2.
        var r = Q("wire transfer");
        Assert.Sequence(["d1", "d4"], r.Hits.Select(h => h.Id), "implicit AND keeps only docs with both terms");
        Assert.Near(0.490428 + 0.693147, r.Hits[0].Score);
        Assert.Near(0.313874 + 0.609970, r.Hits[1].Score);

        var or = Q("wire OR transfer");
        Assert.Sequence(["d1", "d4", "d2"], or.Hits.Select(h => h.Id));
    }

    [Test]
    public void Keyword_filters_do_not_change_scores()
    {
        var all = Q("wire").Hits.ToDictionary(h => h.Id, h => h.Score);
        var open = Q("wire AND status:Open");
        Assert.Sequence(["d1", "d4"], open.Hits.Select(h => h.Id));
        foreach (var h in open.Hits) Assert.Near(all[h.Id], h.Score, 1e-9);
    }

    [Test]
    public void Phrase_query_requires_adjacency()
    {
        Assert.Sequence(["d1", "d4"], Q("\"wire transfer\"").Hits.Select(h => h.Id));
        Assert.Equal(0, Q("\"transfer wire\"").Total);
        Assert.Equal(0, Q("\"wire payment late\"").Total);
    }

    [Test]
    public void Phrase_respects_removed_stopword_gap()
    {
        using var idx = new SearchIndex(new IndexSchema(new Dictionary<string, double> { ["body"] = 1 }, []));
        idx.Upsert(new IndexDocument("a").AddText("body", "Bank of Genoa statement"));
        idx.Upsert(new IndexDocument("b").AddText("body", "Bank Genoa statement"));
        Assert.Sequence(["a"], idx.Search(new SearchRequest { Query = "\"bank of genoa\"" }).Hits.Select(h => h.Id));
        Assert.Sequence(["b"], idx.Search(new SearchRequest { Query = "\"bank genoa\"" }).Hits.Select(h => h.Id));
    }

    [Test]
    public void Phrase_does_not_match_across_values_of_a_multivalued_field()
    {
        using var idx = new SearchIndex(new IndexSchema(new Dictionary<string, double> { ["body"] = 1 }, []));
        idx.Upsert(new IndexDocument("x").AddText("body", "payment from alpha").AddText("body", "beta report"));
        Assert.Equal(0, idx.Search(new SearchRequest { Query = "\"alpha beta\"" }).Total);
        Assert.Equal(1, idx.Search(new SearchRequest { Query = "alpha beta" }).Total);
    }

    [Test]
    public void Not_excludes_and_pure_negation_matches_the_rest()
    {
        Assert.Sequence(["d2"], Q("wire NOT transfer").Hits.Select(h => h.Id));
        Assert.Sequence(["d3"], Q("NOT wire").Hits.Select(h => h.Id));
        Assert.Sequence(["d3"], Q("-wire").Hits.Select(h => h.Id));
    }

    [Test]
    public void Incremental_update_and_remove_keep_statistics_consistent()
    {
        _index.Upsert(new IndexDocument("d2").AddText("body", "card payment"));  // d2 no longer mentions wire
        var r = Q("wire");
        Assert.Sequence(["d1", "d4"], r.Hits.Select(h => h.Id));
        Assert.Near(Bm25.Idf(4, 2), Bm25.Idf(_index.Count, 2), 1e-9);
        Assert.True(_index.Remove("d1"));
        Assert.False(_index.Remove("d1"));
        Assert.Sequence(["d4"], Q("wire").Hits.Select(h => h.Id));
        Assert.Equal(3, _index.Count);
    }

    [Test]
    public void Highlighting_wraps_original_surface_forms()
    {
        using var idx = new SearchIndex(new IndexSchema(new Dictionary<string, double> { ["body"] = 1 }, []));
        idx.Upsert(new IndexDocument("h").AddText("body", "Rilevati bonifici verso la Società Pellara nel mese di marzo."));
        var hit = idx.Search(new SearchRequest { Query = "societa bonifico" }).Hits.Single();
        Assert.Equal("Rilevati [bonifici] verso la [Società] Pellara nel mese di marzo.", hit.Snippets["body"]);
    }

    [Test]
    public void Concurrent_writes_and_reads_are_safe()
    {
        using var idx = new SearchIndex(new IndexSchema(new Dictionary<string, double> { ["body"] = 1 }, ["status"]));
        var errors = 0;
        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            try
            {
                if (i % 2 == 0)
                    idx.Upsert(new IndexDocument($"c{i}").AddText("body", $"transfer number {i} wire").AddKeyword("status", i % 4 == 0 ? "Open" : "Closed"));
                else
                    _ = idx.Search(new SearchRequest { Query = "wire AND transfer", Facets = ["status"] });
            }
            catch { Interlocked.Increment(ref errors); }
        });
        Assert.Equal(0, errors);
        Assert.Equal(200, idx.Search(new SearchRequest { Query = "wire" }).Total);
    }
}
