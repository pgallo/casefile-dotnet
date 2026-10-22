using Casefile.Search.Querying;

namespace Casefile.Tests;

public class QueryParserTests
{
    private static string P(string q) => QueryParser.Parse(q).ToString();

    [Test]
    public void Implicit_and_with_field_value() =>
        Assert.Equal("(AND wire transfer status:Open)", P("wire transfer AND status:Open"));

    [Test]
    public void And_binds_tighter_than_or() =>
        Assert.Equal("(OR a (AND b c))", P("a OR b c"));

    [Test]
    public void Parentheses_override_precedence() =>
        Assert.Equal("(AND (OR a b) c)", P("(a OR b) c"));

    [Test]
    public void Not_and_minus_prefix()
    {
        Assert.Equal("(NOT x)", P("NOT x"));
        Assert.Equal("(AND (NOT x) y)", P("-x y"));
        Assert.Equal("(AND a (NOT (NOT b)))", P("a NOT -b"));
    }

    [Test]
    public void Quoted_phrases_with_and_without_field() =>
        Assert.Equal("(OR title:\"bank of genoa\" (AND cash (NOT tag:low-signal)))", P("title:\"bank of genoa\" OR (cash AND NOT tag:low-signal)"));

    [Test]
    public void Lowercase_operators_are_plain_terms() =>
        Assert.Equal("(AND cats and dogs)", P("cats and dogs"));

    [Test]
    public void Hyphenated_words_and_emails_stay_one_term() =>
        Assert.Equal("(AND co-operation a.b@x.example)", P("co-operation a.b@x.example"));

    [Test]
    public void Empty_query_matches_all() => Assert.Equal("*", P("   "));

    [Test]
    public void Syntax_errors_report_position()
    {
        Assert.Equal(5, Assert.Throws<QueryParseException>(() => QueryParser.Parse("wire \"open")).Position);
        Assert.Throws<QueryParseException>(() => QueryParser.Parse("(a OR b"));
        Assert.Throws<QueryParseException>(() => QueryParser.Parse("a OR"));
        Assert.Throws<QueryParseException>(() => QueryParser.Parse("status:"));
        Assert.Throws<QueryParseException>(() => QueryParser.Parse("a )"));
    }
}
