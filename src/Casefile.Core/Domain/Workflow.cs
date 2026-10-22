namespace Casefile.Core.Domain;

/// <summary>
/// Status workflow as an explicit transition table. Each allowed edge carries its own guard; any edge
/// not in the table is rejected. Keeping the rules in one table (instead of scattered if-statements in
/// endpoints) makes them testable and lets the API list allowed next states.
/// </summary>
public static class Workflow
{
    private delegate string? Guard(Case c, string actor, string? reason);

    private static readonly Dictionary<(CaseStatus From, CaseStatus To), (string Rule, Guard Check)> Edges = new()
    {
        [(CaseStatus.Open, CaseStatus.InReview)] = ("submit-for-review", (c, _, _) =>
            c.Documents.Count == 0 ? "A case needs at least one document before review."
            : string.IsNullOrWhiteSpace(c.Assignee) ? "A case needs an assignee before review."
            : null),

        [(CaseStatus.InReview, CaseStatus.Closed)] = ("close", (c, actor, reason) =>
            !c.Documents.Any(d => d.Kind == DocumentKind.Report) ? "Closing requires at least one Report document."
            : string.Equals(actor, c.Assignee, StringComparison.OrdinalIgnoreCase) ? "Four-eyes rule: the assignee cannot close their own case."
            : string.IsNullOrWhiteSpace(reason) ? "Closing requires a reason."
            : null),

        [(CaseStatus.InReview, CaseStatus.Open)] = ("return-to-open", (_, _, reason) =>
            string.IsNullOrWhiteSpace(reason) ? "Returning a case to Open requires a reason." : null),

        [(CaseStatus.Closed, CaseStatus.Open)] = ("reopen", (_, _, reason) =>
            string.IsNullOrWhiteSpace(reason) ? "Reopening requires a reason." : null),
    };

    public static IReadOnlyList<CaseStatus> AllowedTargets(CaseStatus from) =>
        Edges.Keys.Where(k => k.From == from).Select(k => k.To).ToList();

    /// <summary>Throws <see cref="WorkflowViolationException"/> if the transition is not allowed; returns the rule name otherwise.</summary>
    public static string EnsureCanTransition(Case c, CaseStatus to, string actor, string? reason)
    {
        if (!Edges.TryGetValue((c.Status, to), out var edge))
        {
            var allowed = AllowedTargets(c.Status);
            throw new WorkflowViolationException("invalid-transition",
                $"Cannot move a case from {c.Status} to {to}. Allowed: {(allowed.Count == 0 ? "none" : string.Join(", ", allowed))}.");
        }
        var error = edge.Check(c, actor, reason);
        if (error is not null) throw new WorkflowViolationException(edge.Rule, error);
        return edge.Rule;
    }

    /// <summary>Closed cases are read-only; the only permitted change is the reopen transition.</summary>
    public static void EnsureEditable(Case c)
    {
        if (c.Status == CaseStatus.Closed)
            throw new WorkflowViolationException("closed-is-read-only", $"Case {c.Id} is Closed; reopen it before editing.");
    }
}
