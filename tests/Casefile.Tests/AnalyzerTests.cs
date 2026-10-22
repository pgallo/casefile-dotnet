using Casefile.Search.Analysis;

namespace Casefile.Tests;

public class AnalyzerTests
{
    private static List<string> Terms(string text) => Analyzer.Default.Analyze(text).Select(t => t.Term).ToList();

    [Test]
    public void Tokenizer_splits_on_punctuation_and_italian_elision()
    {
        var tokens = Tokenizer.Tokenize("dell'ufficio, IBAN: IT60-X054").Select(t => t.Term).ToList();
        Assert.Sequence(["dell", "ufficio", "IBAN", "IT60", "X054"], tokens);
    }

    [Test]
    public void Tokenizer_reports_character_offsets()
    {
        var t = Tokenizer.Tokenize("  Società  Pellara").ToList();
        Assert.Equal(2, t[0].Start);
        Assert.Equal(9, t[0].End);
        Assert.Equal("Pellara", "  Società  Pellara"[t[1].Start..t[1].End]);
    }

    [Test]
    public void Accent_folding_and_lowercasing_unify_spellings()
    {
        Assert.Equal("societa", AccentFolder.Fold("società"));
        Assert.Equal("strasse", AccentFolder.Fold("straße"));
        Assert.Sequence(Terms("SOCIETÀ"), Terms("societa"));
        Assert.Sequence(Terms("Perché"), Terms("perche"));
    }

    [Test]
    public void Stopwords_are_removed_for_english_and_italian_but_positions_are_kept()
    {
        var tokens = Analyzer.Default.Analyze("Le società di Genova and the bank");
        Assert.Sequence(["societ", "genov", "bank"], tokens.Select(t => t.Term));
        Assert.Sequence([1, 3, 6], tokens.Select(t => t.Position));
    }

    [Test]
    public void Light_stemmer_collapses_common_inflections()
    {
        Assert.Equal(LightStemmer.Stem("transfer"), LightStemmer.Stem("transfers"));
        Assert.Equal(LightStemmer.Stem("transferring"), LightStemmer.Stem("transferred"));
        Assert.Equal(LightStemmer.Stem("bonifico"), LightStemmer.Stem("bonifici"));
        Assert.Equal(LightStemmer.Stem("bianco"), LightStemmer.Stem("bianchi"));
        Assert.Equal("company", LightStemmer.Stem("companies"));
        Assert.Equal("business", LightStemmer.Stem("business"));
        Assert.Equal("wire", LightStemmer.Stem("wires"));
        Assert.Equal("2026", LightStemmer.Stem("2026"));
    }
}
