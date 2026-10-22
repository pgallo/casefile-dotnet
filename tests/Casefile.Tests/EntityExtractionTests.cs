using Casefile.Core.Entities;
using Casefile.Core.Seed;

namespace Casefile.Tests;

public class EntityExtractionTests
{
    private readonly EntityExtractor _x = SyntheticData.BuildExtractor();

    private List<string> Found(string text) => _x.ExtractMentions(text, "t").Select(m => $"{m.Type}:{m.Key}").ToList();

    [Test]
    public void Iban_checksum_is_enforced()
    {
        var valid = Iban.Create("IT", "X0999901000000000071234");
        Assert.True(Iban.IsValid(valid));
        Assert.True(Iban.IsValid(Iban.Pretty(valid)));
        var broken = valid[..^1] + (valid[^1] == '9' ? '8' : '9');
        Assert.False(Iban.IsValid(broken));
    }

    [Test]
    public void Identifiers_are_extracted_and_normalized()
    {
        var iban = Iban.Create("MT", "VALL12345000000000012345678");
        var text = $"Paid to {Iban.Pretty(iban)} BEFORE noon. Call +44 7700 900417 or mail A.Ferrantelli@Pellara.example today.";
        var found = Found(text);
        Assert.Contains($"Iban:{iban}", found);
        Assert.Contains("Phone:+447700900417", found);
        Assert.Contains("Email:a.ferrantelli@pellara.example", found);
    }

    [Test]
    public void Invalid_account_like_strings_and_short_numbers_are_ignored()
    {
        var found = Found("Ref IT00 1234 5678 9012 3456 78 and invoice +39 12 on 2026-03-01.");
        Assert.False(found.Any(f => f.StartsWith("Iban:", StringComparison.Ordinal)));
        Assert.False(found.Any(f => f.StartsWith("Phone:", StringComparison.Ordinal)));
    }

    [Test]
    public void Gazetteer_matches_folded_aliases_with_longest_match()
    {
        var found = Found("Payments from SOCIETA AGRICOLA FONTEBELLA reached Genoa; Fontebella denies it.");
        Assert.Contains("Organisation:societa agricola fontebella", found);
        Assert.Contains("Location:genova", found);
        Assert.Equal(2, found.Count(f => f == "Organisation:societa agricola fontebella"), "the alias maps to the canonical key");
    }

    [Test]
    public void Pattern_rules_find_unknown_people_and_organisations()
    {
        var found = Found("Meeting between Dott. Marco Ventrella and the director of Orsolina Logistica S.r.l. in Valletta.");
        Assert.Contains("Person:marco ventrella", found);
        Assert.Contains("Organisation:orsolina logistica", found);
        Assert.Contains("Location:valletta", found);
    }

    [Test]
    public void Aggregation_counts_mentions_per_entity()
    {
        var entities = _x.Extract([("a", "Aldo Ferrantelli met Giulia Marchetto."), ("b", "Aldo Ferrantelli flew to Napoli.")]);
        var aldo = entities.Single(e => e.Key == "aldo ferrantelli");
        Assert.Equal(2, aldo.Mentions);
        Assert.Equal(EntityType.Person, aldo.Type);
        Assert.Equal(3, entities.Count);
    }
}
