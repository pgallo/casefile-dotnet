namespace Casefile.Search.Querying;

/// <summary>Query AST. <c>ToString</c> gives a canonical, fully parenthesized form used in tests and debugging.</summary>
public abstract record QueryNode;

/// <summary>A bare word or a <c>field:word</c>. Resolved against the schema at execution time.</summary>
public sealed record TermNode(string? Field, string Text) : QueryNode
{
    public override string ToString() => Field is null ? Text : $"{Field}:{Text}";
}

/// <summary>A quoted phrase, optionally field-scoped.</summary>
public sealed record PhraseNode(string? Field, string Text) : QueryNode
{
    public override string ToString() => Field is null ? $"\"{Text}\"" : $"{Field}:\"{Text}\"";
}

public sealed record AndNode(IReadOnlyList<QueryNode> Clauses) : QueryNode
{
    public override string ToString() => $"(AND {string.Join(' ', Clauses)})";
}

public sealed record OrNode(IReadOnlyList<QueryNode> Clauses) : QueryNode
{
    public override string ToString() => $"(OR {string.Join(' ', Clauses)})";
}

public sealed record NotNode(QueryNode Inner) : QueryNode
{
    public override string ToString() => $"(NOT {Inner})";
}

/// <summary>Matches every document. Produced by an empty query.</summary>
public sealed record MatchAllNode : QueryNode
{
    public static readonly MatchAllNode Instance = new();
    public override string ToString() => "*";
}
