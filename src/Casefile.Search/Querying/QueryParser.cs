using System.Text;

namespace Casefile.Search.Querying;

public sealed class QueryParseException(string message, int position)
    : Exception($"{message} (at position {position})")
{
    public int Position { get; } = position;
}

/// <summary>
/// Recursive descent parser for the analyst query language.
/// <code>
/// query   := or
/// or      := and ( "OR" and )*
/// and     := unary ( "AND"? unary )*        adjacent clauses are an implicit AND
/// unary   := ( "NOT" | "-" ) unary | primary
/// primary := "(" or ")" | ( FIELD ":" )? ( WORD | "quoted phrase" )
/// </code>
/// Operators are case-sensitive uppercase so that the lowercase words "and", "or", "not" stay searchable
/// (and are then removed as stopwords, like any other text).
/// </summary>
public static class QueryParser
{
    public static QueryNode Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return MatchAllNode.Instance;
        var tokens = Lex(input);
        var p = new Parser(tokens);
        var node = p.ParseOr();
        if (!p.AtEnd) throw new QueryParseException($"Unexpected '{p.Peek.Text}'", p.Peek.Position);
        return node;
    }

    private enum Kind { Word, Phrase, LParen, RParen, And, Or, Not, Colon, End }

    private readonly record struct Lexeme(Kind Kind, string Text, int Position);

    private static List<Lexeme> Lex(string s)
    {
        var result = new List<Lexeme>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            switch (c)
            {
                case '(': result.Add(new(Kind.LParen, "(", i++)); continue;
                case ')': result.Add(new(Kind.RParen, ")", i++)); continue;
                case ':': result.Add(new(Kind.Colon, ":", i++)); continue;
                case '-' when result.Count == 0 || result[^1].Kind is not (Kind.Colon):
                    result.Add(new(Kind.Not, "-", i++)); continue;
                case '"':
                {
                    var start = i++;
                    var sb = new StringBuilder();
                    while (i < s.Length && s[i] != '"') sb.Append(s[i++]);
                    if (i >= s.Length) throw new QueryParseException("Unterminated quote", start);
                    i++;
                    result.Add(new(Kind.Phrase, sb.ToString(), start));
                    continue;
                }
            }
            var wStart = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('(' or ')' or ':' or '"')) i++;
            var word = s[wStart..i];
            var kind = word switch { "AND" or "&&" => Kind.And, "OR" or "||" => Kind.Or, "NOT" => Kind.Not, _ => Kind.Word };
            result.Add(new(kind, word, wStart));
        }
        result.Add(new(Kind.End, "<end>", s.Length));
        return result;
    }

    private sealed class Parser(List<Lexeme> tokens)
    {
        private int _pos;

        public Lexeme Peek => tokens[_pos];
        public bool AtEnd => Peek.Kind == Kind.End;
        private Lexeme Next() => tokens[_pos++];

        public QueryNode ParseOr()
        {
            var clauses = new List<QueryNode> { ParseAnd() };
            while (Peek.Kind == Kind.Or)
            {
                Next();
                clauses.Add(ParseAnd());
            }
            return clauses.Count == 1 ? clauses[0] : new OrNode(clauses);
        }

        private QueryNode ParseAnd()
        {
            var clauses = new List<QueryNode> { ParseUnary() };
            while (true)
            {
                if (Peek.Kind == Kind.And) { Next(); clauses.Add(ParseUnary()); continue; }
                if (Peek.Kind is Kind.Word or Kind.Phrase or Kind.LParen or Kind.Not) { clauses.Add(ParseUnary()); continue; }
                break;
            }
            return clauses.Count == 1 ? clauses[0] : new AndNode(clauses);
        }

        private QueryNode ParseUnary()
        {
            if (Peek.Kind == Kind.Not)
            {
                Next();
                return new NotNode(ParseUnary());
            }
            return ParsePrimary();
        }

        private QueryNode ParsePrimary()
        {
            var t = Next();
            switch (t.Kind)
            {
                case Kind.LParen:
                {
                    var inner = ParseOr();
                    var close = Next();
                    if (close.Kind != Kind.RParen) throw new QueryParseException("Expected ')'", close.Position);
                    return inner;
                }
                case Kind.Phrase:
                    return new PhraseNode(null, t.Text);
                case Kind.Word when Peek.Kind == Kind.Colon:
                {
                    Next();
                    var value = Next();
                    return value.Kind switch
                    {
                        Kind.Word => new TermNode(t.Text, value.Text),
                        Kind.Phrase => new PhraseNode(t.Text, value.Text),
                        _ => throw new QueryParseException($"Expected a value after '{t.Text}:'", value.Position),
                    };
                }
                case Kind.Word:
                    return new TermNode(null, t.Text);
                default:
                    throw new QueryParseException($"Unexpected '{t.Text}'", t.Position);
            }
        }
    }
}
