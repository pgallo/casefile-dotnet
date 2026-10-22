using Casefile.Search.Analysis;
using Casefile.Search.Indexing;

namespace Casefile.Search.Querying;

public sealed class UnknownFieldException(string field)
    : Exception($"Unknown field '{field}'")
{
    public string Field { get; } = field;
}

/// <summary>
/// Evaluates a query AST against the inverted index, producing a doc -> score map.
/// AND sums clause scores over the intersection, OR sums over the union, NOT subtracts.
/// Keyword-field clauses (status:Open) filter without contributing to the score, so filtering a query
/// never changes the relative order of its results.
/// </summary>
internal sealed class QueryExecutor(InvertedIndex index, Bm25 bm25)
{
    /// <summary>Result of evaluating a node. <c>Neutral</c> means "imposes no constraint" (e.g. a query made only of stopwords).</summary>
    private sealed record Eval(Dictionary<int, double> Scores, bool Neutral = false)
    {
        public static Eval NeutralResult { get; } = new([], true);
    }

    public Dictionary<int, double> Execute(QueryNode node)
    {
        var result = Evaluate(node);
        return result.Neutral ? index.AllDocs.ToDictionary(d => d, _ => 0.0) : result.Scores;
    }

    /// <summary>Analyzed terms that can match positively, per text field, for highlighting. NOT subtrees are skipped.</summary>
    public HashSet<string> PositiveTerms(QueryNode node)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        Collect(node, terms);
        return terms;

        void Collect(QueryNode n, HashSet<string> acc)
        {
            switch (n)
            {
                case TermNode t when t.Field is null || index.Schema.IsText(t.Field):
                    foreach (var tok in index.Analyzer.Analyze(t.Text)) acc.Add(tok.Term);
                    break;
                case PhraseNode p when p.Field is null || index.Schema.IsText(p.Field):
                    foreach (var tok in index.Analyzer.Analyze(p.Text)) acc.Add(tok.Term);
                    break;
                case AndNode a: foreach (var c in a.Clauses) Collect(c, acc); break;
                case OrNode o: foreach (var c in o.Clauses) Collect(c, acc); break;
            }
        }
    }

    private Eval Evaluate(QueryNode node) => node switch
    {
        MatchAllNode => Eval.NeutralResult,
        TermNode t => EvaluateTerm(t),
        PhraseNode p => EvaluatePhrase(p),
        AndNode a => EvaluateAnd(a),
        OrNode o => EvaluateOr(o),
        NotNode n => EvaluateNot(n),
        _ => throw new NotSupportedException(node.GetType().Name),
    };

    private IEnumerable<string> TargetTextFields(string? field)
    {
        if (field is null) return index.Schema.TextFields;
        return [index.Schema.Canonical(field)];
    }

    private void ValidateField(string? field)
    {
        if (field is not null && !index.Schema.IsText(field) && !index.Schema.IsKeyword(field))
            throw new UnknownFieldException(field);
    }

    private Eval EvaluateTerm(TermNode t)
    {
        ValidateField(t.Field);
        if (t.Field is not null && index.Schema.IsKeyword(t.Field))
            return KeywordMatch(t.Field, t.Text);

        var tokens = index.Analyzer.Analyze(t.Text);
        if (tokens.Count == 0) return Eval.NeutralResult;
        // A single "word" that analyzes into several tokens (an email, "co-operation") behaves like a phrase.
        if (tokens.Count > 1) return PhraseScores(t.Field, tokens);

        var term = tokens[0].Term;
        var scores = new Dictionary<int, double>();
        foreach (var field in TargetTextFields(t.Field))
        {
            var postings = index.Postings(field, term);
            if (postings.Count == 0) continue;
            var idf = Bm25.Idf(index.DocCount, postings.Count);
            var avg = index.AvgFieldLength(field);
            var boost = index.Schema.Boost(field);
            foreach (var (doc, positions) in postings)
            {
                var s = boost * bm25.TermScore(idf, positions.Count, index.FieldLength(field, doc), avg);
                scores[doc] = scores.GetValueOrDefault(doc) + s;
            }
        }
        return new Eval(scores);
    }

    private Eval EvaluatePhrase(PhraseNode p)
    {
        ValidateField(p.Field);
        if (p.Field is not null && index.Schema.IsKeyword(p.Field))
            return KeywordMatch(p.Field, p.Text);

        var tokens = index.Analyzer.Analyze(p.Text);
        if (tokens.Count == 0) return Eval.NeutralResult;
        if (tokens.Count == 1) return EvaluateTerm(new TermNode(p.Field, p.Text));
        return PhraseScores(p.Field, tokens);
    }

    /// <summary>
    /// Positional phrase match. Relative offsets come from the analyzed query, so a removed stopword
    /// ("bank of genoa") still requires the same gap in the document. Scored as BM25 with the phrase
    /// frequency as tf and the summed term IDFs as idf.
    /// </summary>
    private Eval PhraseScores(string? fieldFilter, IReadOnlyList<Token> tokens)
    {
        var scores = new Dictionary<int, double>();
        var basePos = tokens[0].Position;
        foreach (var field in TargetTextFields(fieldFilter))
        {
            var lists = tokens.Select(t => index.Postings(field, t.Term)).ToList();
            if (lists.Any(l => l.Count == 0)) continue;
            var rarest = lists.OrderBy(l => l.Count).First();
            var idf = tokens.Sum(t => Bm25.Idf(index.DocCount, index.DocFreq(field, t.Term)));
            var avg = index.AvgFieldLength(field);
            var boost = index.Schema.Boost(field);

            foreach (var doc in rarest.Keys)
            {
                if (!lists.All(l => l.ContainsKey(doc))) continue;
                var first = lists[0][doc];
                var freq = 0;
                foreach (var start in first)
                {
                    var ok = true;
                    for (var i = 1; i < tokens.Count && ok; i++)
                        ok = lists[i][doc].BinarySearch(start + tokens[i].Position - basePos) >= 0;
                    if (ok) freq++;
                }
                if (freq == 0) continue;
                var s = boost * bm25.TermScore(idf, freq, index.FieldLength(field, doc), avg);
                scores[doc] = scores.GetValueOrDefault(doc) + s;
            }
        }
        return new Eval(scores);
    }

    private Eval KeywordMatch(string field, string value)
    {
        var docs = index.KeywordDocs(index.Schema.Canonical(field), value);
        return new Eval(docs.ToDictionary(d => d, _ => 0.0));
    }

    private Eval EvaluateAnd(AndNode a)
    {
        Dictionary<int, double>? acc = null;
        var excluded = new List<Dictionary<int, double>>();
        foreach (var clause in a.Clauses)
        {
            if (clause is NotNode not)
            {
                var inner = Evaluate(not.Inner);
                if (!inner.Neutral) excluded.Add(inner.Scores);
                continue;
            }
            var e = Evaluate(clause);
            if (e.Neutral) continue;
            if (acc is null)
            {
                acc = new Dictionary<int, double>(e.Scores);
                continue;
            }
            var next = new Dictionary<int, double>();
            foreach (var (doc, s) in acc)
                if (e.Scores.TryGetValue(doc, out var s2)) next[doc] = s + s2;
            acc = next;
        }

        if (acc is null)
        {
            // Only negative (or neutral) clauses: "NOT x AND NOT y" means everything except.
            if (excluded.Count == 0) return Eval.NeutralResult;
            acc = index.AllDocs.ToDictionary(d => d, _ => 0.0);
        }
        foreach (var ex in excluded)
            foreach (var doc in ex.Keys) acc.Remove(doc);
        return new Eval(acc);
    }

    private Eval EvaluateOr(OrNode o)
    {
        var acc = new Dictionary<int, double>();
        var anyConstraint = false;
        foreach (var clause in o.Clauses)
        {
            var e = Evaluate(clause);
            if (e.Neutral) continue;
            anyConstraint = true;
            foreach (var (doc, s) in e.Scores) acc[doc] = acc.GetValueOrDefault(doc) + s;
        }
        return anyConstraint ? new Eval(acc) : Eval.NeutralResult;
    }

    private Eval EvaluateNot(NotNode n)
    {
        var inner = Evaluate(n.Inner);
        if (inner.Neutral) return Eval.NeutralResult;
        var result = new Dictionary<int, double>();
        foreach (var doc in index.AllDocs)
            if (!inner.Scores.ContainsKey(doc)) result[doc] = 0;
        return new Eval(result);
    }
}
