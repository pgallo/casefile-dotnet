namespace Casefile.Search.Analysis;

/// <summary>An analyzed token. Offsets point into the original text so highlighting can wrap the surface form.</summary>
public readonly record struct Token(string Term, int Position, int Start, int End);
