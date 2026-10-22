using Casefile.Core.Entities;
using Casefile.Core.Graph;

namespace Casefile.Tests;

public class RelatedCasesTests
{
    private static ExtractedEntity E(EntityType t, string key) => new(t, key, key);

    /// <summary>
    /// Eight cases. Target A shares one rare IBAN with B, a common city with C..F, and a person with G.
    /// Expected: B (rare hard identifier) above G (person seen in 2 cases) above any city-only match.
    /// </summary>
    private static LinkGraph Graph()
    {
        var g = new LinkGraph { HubFraction = 0.75 };
        var iban = E(EntityType.Iban, "IT00TEST");
        var city = E(EntityType.Location, "genova");
        var person = E(EntityType.Person, "aldo ferrantelli");
        g.SetCase("A", [iban, city, person]);
        g.SetCase("B", [iban]);
        g.SetCase("C", [city]);
        g.SetCase("D", [city]);
        g.SetCase("E", [city]);
        g.SetCase("F", [city]);
        g.SetCase("G", [person, E(EntityType.Phone, "+390000")]);
        g.SetCase("H", [E(EntityType.Phone, "+390000")]);
        return g;
    }

    [Test]
    public void Related_cases_are_ranked_by_idf_weighted_shared_entities()
    {
        var related = Graph().Related("A");
        Assert.Sequence(["B", "G", "C", "D", "E", "F"], related.Select(r => r.CaseId));
        // N = 8. Iban df 2: ln(1 + 8/2) * 1.5 = 2.4142; person df 2: ln(5) * 1.0 = 1.6094; city df 5: ln(1 + 8/5) * 0.4 = 0.3822
        Assert.Near(Math.Log(5) * 1.5, related[0].Score);
        Assert.Near(Math.Log(5), related[1].Score);
        Assert.Near(Math.Log(2.6) * 0.4, related[2].Score);
        Assert.Equal("IT00TEST", related[0].SharedEntities.Single().Key);
    }

    [Test]
    public void Hub_entities_above_the_frequency_ceiling_are_ignored()
    {
        // 8 cases, HubFraction 0.5: ceiling is 4 cases, so the city (in 5 cases) is a hub and links nothing.
        var g = new LinkGraph { HubFraction = 0.5 };
        g.SetCase("A", [E(EntityType.Iban, "X"), E(EntityType.Location, "genova")]);
        g.SetCase("B", [E(EntityType.Iban, "X")]);
        foreach (var id in new[] { "C", "D", "E", "F" }) g.SetCase(id, [E(EntityType.Location, "genova")]);
        g.SetCase("G", []);
        g.SetCase("H", []);
        Assert.Sequence(["B"], g.Related("A").Select(r => r.CaseId));
        Assert.Equal(6, Graph().Related("A").Count, "with a looser ceiling the city links C..F as well");
    }

    [Test]
    public void Updating_and_removing_a_case_updates_links()
    {
        var g = Graph();
        g.SetCase("B", []);
        Assert.DoesNotContain("B", g.Related("A").Select(r => r.CaseId));
        g.RemoveCase("G");
        Assert.DoesNotContain("G", g.Related("A").Select(r => r.CaseId));
        Assert.Sequence(["A"], g.CasesFor(EntityType.Person, "aldo ferrantelli"));
        Assert.Equal(0, g.Related("missing").Count);
    }
}
