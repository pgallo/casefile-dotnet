using System.Text.RegularExpressions;

namespace Casefile.Core.Entities;

/// <summary>
/// Hybrid extractor: regular expressions for identifiers with a recognizable shape (email, phone,
/// account numbers with a checksum), a gazetteer for known names, and two pattern rules that propose
/// names the gazetteer does not know yet (honorific + capitalized name, capitalized name + legal suffix).
/// Overlapping mentions are resolved by precedence: identifiers, then gazetteer, then pattern rules.
/// </summary>
public sealed partial class EntityExtractor(Gazetteer gazetteer)
{
    [GeneratedRegex(@"(?<![\w.+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}(?!\w)")]
    private static partial Regex EmailRegex();

    // International format only (+CC followed by 8 to 12 digits, optional separators). Local formats
    // are too ambiguous with dates, amounts and reference numbers to extract without context.
    [GeneratedRegex(@"(?<![\w+])\+\d{2,3}(?:[ .\-]?\(?\d+\)?){2,5}(?![\w])")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"(?<![A-Z0-9])[A-Z]{2}\d{2}(?: ?[A-Z0-9]{1,4}){3,8}")]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"\b(?:Mr|Mrs|Ms|Dr|Sig|Sig\.ra|Dott|Dott\.ssa|Avv|Ing)\.?\s+(?<name>\p{Lu}[\p{Ll}']+(?:\s+\p{Lu}[\p{Ll}']+){0,2})")]
    private static partial Regex HonorificPersonRegex();

    [GeneratedRegex(@"\b(?<name>\p{Lu}[\p{L}&]+(?:\s+\p{Lu}[\p{L}&]+){0,3})\s+(?<suffix>S\.r\.l\.|S\.p\.A\.|Srl|SpA|Ltd|Limited|GmbH|B\.V\.|LLC)(?![\w])")]
    private static partial Regex LegalSuffixOrgRegex();

    private static readonly HashSet<string> LeadingNoise = new(StringComparer.OrdinalIgnoreCase) { "The", "La", "Il", "Lo", "Della", "Dalla", "From", "By", "To" };

    public IReadOnlyList<EntityMention> ExtractMentions(string text, string source)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var accepted = new List<EntityMention>();

        void Offer(EntityMention m)
        {
            if (accepted.Any(a => m.Start < a.End && a.Start < m.End)) return;
            accepted.Add(m);
        }

        foreach (Match m in EmailRegex().Matches(text))
            Offer(new(EntityType.Email, m.Value.ToLowerInvariant(), m.Value.ToLowerInvariant(), m.Index, m.Index + m.Length, source));

        foreach (Match m in IbanRegex().Matches(text))
            if (TrimToValidIban(m.Value) is { } iban)
                Offer(new(EntityType.Iban, Iban.Normalize(iban), Iban.Pretty(iban), m.Index, m.Index + iban.Length, source));

        foreach (Match m in PhoneRegex().Matches(text))
        {
            var digits = new string(m.Value.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length is < 10 or > 14) continue;
            Offer(new(EntityType.Phone, "+" + digits, m.Value.Trim(), m.Index, m.Index + m.Length, source));
        }

        foreach (var m in gazetteer.Match(text, source)) Offer(m);

        foreach (Match m in HonorificPersonRegex().Matches(text))
        {
            var g = m.Groups["name"];
            Offer(new(EntityType.Person, Gazetteer.KeyOf(g.Value), g.Value, g.Index, g.Index + g.Length, source));
        }

        foreach (Match m in LegalSuffixOrgRegex().Matches(text))
        {
            var name = m.Groups["name"].Value;
            var start = m.Groups["name"].Index;
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            while (words.Count > 1 && LeadingNoise.Contains(words[0]))
            {
                start += name.IndexOf(words[1], StringComparison.Ordinal) - name.IndexOf(words[0], StringComparison.Ordinal);
                words.RemoveAt(0);
            }
            var display = $"{string.Join(' ', words)} {m.Groups["suffix"].Value}";
            // Key ignores the legal suffix so "Pellara Srl" and "Pellara S.r.l." are one node.
            Offer(new(EntityType.Organisation, Gazetteer.KeyOf(string.Join(' ', words)), display, start, m.Index + m.Length, source));
        }

        return accepted.OrderBy(a => a.Start).ToList();
    }

    /// <summary>Aggregates mentions from several texts into one entry per (type, key), counting mentions.</summary>
    public IReadOnlyList<ExtractedEntity> Extract(IEnumerable<(string Source, string Text)> texts) =>
        texts.SelectMany(t => ExtractMentions(t.Text, t.Source))
            .GroupBy(m => (m.Type, m.Key))
            .Select(g => new ExtractedEntity(g.Key.Type, g.Key.Key, g.First().Display, g.Count()))
            .OrderBy(e => e.Type).ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The IBAN regex is greedy and may run into a following uppercase word ("IT60 X054 ... VIA").
    /// Shrink from the right one character at a time until the checksum validates.
    /// </summary>
    private static string? TrimToValidIban(string candidate)
    {
        for (var end = candidate.Length; end >= 15; end--)
        {
            var s = candidate[..end].TrimEnd();
            if (s.Length != end) continue;
            if (Iban.IsValid(s)) return s;
        }
        return null;
    }
}
