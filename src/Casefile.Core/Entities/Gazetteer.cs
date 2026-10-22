using Casefile.Search.Analysis;

namespace Casefile.Core.Entities;

public sealed record GazetteerEntry(EntityType Type, string Canonical, IReadOnlyList<string> Aliases);

/// <summary>
/// Dictionary matcher over a token trie. Surface forms are tokenized and folded (lowercase, no accents)
/// so "Società Pellara" matches "societa pellara". Matching is greedy longest-match from each token,
/// which makes "Banca Valdenza Genova" win over the shorter "Valdenza" alias when both are listed.
/// </summary>
public sealed class Gazetteer
{
    private sealed class Node
    {
        public Dictionary<string, Node> Next { get; } = new(StringComparer.Ordinal);
        public GazetteerEntry? Entry { get; set; }
    }

    private readonly Node _root = new();

    public Gazetteer(IEnumerable<GazetteerEntry> entries)
    {
        foreach (var e in entries)
            foreach (var surface in e.Aliases.Prepend(e.Canonical))
                Add(surface, e);
    }

    public static string KeyOf(string canonical) =>
        string.Join(' ', Tokenizer.Tokenize(canonical).Select(t => AccentFolder.Fold(t.Term.ToLowerInvariant())));

    private void Add(string surface, GazetteerEntry entry)
    {
        var node = _root;
        foreach (var t in Tokenizer.Tokenize(surface))
        {
            var key = AccentFolder.Fold(t.Term.ToLowerInvariant());
            if (!node.Next.TryGetValue(key, out var child)) node.Next[key] = child = new Node();
            node = child;
        }
        if (node != _root) node.Entry ??= entry;
    }

    public IEnumerable<EntityMention> Match(string text, string source)
    {
        var tokens = Tokenizer.Tokenize(text).ToList();
        var folded = tokens.Select(t => AccentFolder.Fold(t.Term.ToLowerInvariant())).ToList();
        var i = 0;
        while (i < tokens.Count)
        {
            var node = _root;
            GazetteerEntry? best = null;
            var bestEnd = -1;
            for (var j = i; j < tokens.Count && node.Next.TryGetValue(folded[j], out var next); j++)
            {
                node = next;
                if (node.Entry is not null) { best = node.Entry; bestEnd = j; }
            }
            if (best is null) { i++; continue; }
            // Require the first matched token to be capitalized in the text, to avoid matching common words
            // that happen to equal an alias (a location alias like "Bari" written as a lowercase noun, etc.).
            if (char.IsUpper(tokens[i].Term[0]))
                yield return new EntityMention(best.Type, KeyOf(best.Canonical), best.Canonical, tokens[i].Start, tokens[bestEnd].End, source);
            i = bestEnd + 1;
        }
    }
}
