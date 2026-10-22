using Casefile.Search;
using Casefile.Search.Indexing;
using Casefile.Search.Querying;

namespace Casefile.Tests;

public class FacetTests : IDisposable
{
    private readonly SearchIndex _index = new(new IndexSchema(new Dictionary<string, double> { ["body"] = 1 }, ["status", "tag"]));

    public FacetTests()
    {
        Add("1", "wire transfer", "Open", "aml", "wire");
        Add("2", "wire transfer", "Open", "aml");
        Add("3", "wire transfer", "Closed", "fraud");
        Add("4", "wire transfer", "InReview", "aml", "fraud");
        Add("5", "cash deposit", "Open", "aml");
    }

    private void Add(string id, string body, string status, params string[] tags)
    {
        var d = new IndexDocument(id).AddText("body", body).AddKeyword("status", status);
        foreach (var t in tags) d.AddKeyword("tag", t);
        _index.Upsert(d);
    }

    public void Dispose() => _index.Dispose();

    private static string Fmt(IEnumerable<FacetValue> values) => string.Join(" ", values.Select(v => $"{v.Value}={v.Count}"));

    [Test]
    public void Facet_counts_cover_the_matching_set_sorted_by_count()
    {
        var r = _index.Search(new SearchRequest { Query = "wire", Facets = ["status", "tag"] });
        Assert.Equal(4, r.Total);
        Assert.Equal("Open=2 Closed=1 InReview=1", Fmt(r.Facets["status"]));
        Assert.Equal("aml=3 fraud=2 wire=1", Fmt(r.Facets["tag"]));
    }

    [Test]
    public void Filters_are_or_within_a_field_and_and_across_fields()
    {
        var r = _index.Search(new SearchRequest
        {
            Query = "wire",
            Filters = new Dictionary<string, IReadOnlyList<string>> { ["status"] = ["open", "inreview"], ["tag"] = ["aml"] },
        });
        Assert.Sequence(["1", "2", "4"], r.Hits.Select(h => h.Id).Order());
    }

    [Test]
    public void Multi_select_facets_ignore_their_own_filter()
    {
        var r = _index.Search(new SearchRequest
        {
            Query = "wire",
            Filters = new Dictionary<string, IReadOnlyList<string>> { ["status"] = ["Open"] },
            Facets = ["status", "tag"],
        });
        Assert.Equal(2, r.Total);
        // status counts still show alternatives the user could switch to...
        Assert.Equal("Open=2 Closed=1 InReview=1", Fmt(r.Facets["status"]));
        // ...while other facets are narrowed by the status filter.
        Assert.Equal("aml=2 wire=1", Fmt(r.Facets["tag"]));
    }

    [Test]
    public void Unknown_facet_or_filter_field_is_rejected()
    {
        Assert.Throws<UnknownFieldException>(() => _index.Search(new SearchRequest { Facets = ["body"] }));
        Assert.Throws<UnknownFieldException>(() => _index.Search(new SearchRequest { Query = "colour:red" }));
    }
}
