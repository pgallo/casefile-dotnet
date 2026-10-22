using System.Text;
using Casefile.Search.Analysis;

namespace Casefile.Search.Querying;

/// <summary>
/// Builds a snippet around the densest cluster of matched terms. The text is re-analyzed to recover
/// character offsets, so the markers wrap the original surface form ("Società") even though the
/// match happened on the folded, stemmed term ("societ").
/// </summary>
public sealed class Highlighter(Analyzer analyzer, string preTag = "[", string postTag = "]", int windowTokens = 18)
{
    public string? Snippet(string text, IReadOnlySet<string> terms)
    {
        if (terms.Count == 0 || string.IsNullOrEmpty(text)) return null;
        var tokens = analyzer.Analyze(text);
        var hits = new List<int>();
        for (var i = 0; i < tokens.Count; i++)
            if (terms.Contains(tokens[i].Term)) hits.Add(i);
        if (hits.Count == 0) return null;

        // Sliding window over hit indices: choose the start that covers the most hits within windowTokens positions.
        int bestStart = 0, bestCount = 0, j = 0;
        for (var i = 0; i < hits.Count; i++)
        {
            while (tokens[hits[i]].Position - tokens[hits[j]].Position >= windowTokens) j++;
            if (i - j + 1 > bestCount) { bestCount = i - j + 1; bestStart = j; }
        }

        var firstHit = tokens[hits[bestStart]];
        var lastHit = tokens[hits[bestStart + bestCount - 1]];
        var slack = Math.Max(0, windowTokens - (lastHit.Position - firstHit.Position)) / 2;
        var startChar = CharOffsetBefore(text, firstHit.Start, slack);
        var endChar = CharOffsetAfter(text, lastHit.End, slack);

        var sb = new StringBuilder();
        if (startChar > 0) sb.Append("...");
        var cursor = startChar;
        foreach (var h in hits)
        {
            var t = tokens[h];
            if (t.Start < startChar || t.End > endChar) continue;
            sb.Append(text, cursor, t.Start - cursor).Append(preTag).Append(text, t.Start, t.End - t.Start).Append(postTag);
            cursor = t.End;
        }
        sb.Append(text, cursor, endChar - cursor);
        if (endChar < text.Length) sb.Append("...");
        return Collapse(sb.ToString());
    }

    /// <summary>Moves back <paramref name="words"/> whitespace-separated words from <paramref name="from"/>.</summary>
    private static int CharOffsetBefore(string text, int from, int words)
    {
        var i = from;
        for (var w = 0; w < words && i > 0; w++)
        {
            while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
            while (i > 0 && !char.IsWhiteSpace(text[i - 1])) i--;
        }
        return i;
    }

    private static int CharOffsetAfter(string text, int from, int words)
    {
        var i = from;
        for (var w = 0; w < words && i < text.Length; w++)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        }
        return i;
    }

    private static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) { if (!space) sb.Append(' '); space = true; }
            else { sb.Append(c); space = false; }
        }
        return sb.ToString().Trim();
    }
}
