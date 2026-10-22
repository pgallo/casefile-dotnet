using Casefile.Core.Domain;

namespace Casefile.Tests;

public class WorkflowTests
{
    [Test]
    public void Happy_path_open_review_close_reopen_with_audit_trail()
    {
        var clock = TestCorpus.Clock();
        using var s = TestCorpus.EmptyService(clock);
        var c = TestCorpus.NewCase(s, "Wire anomaly", "Transfers to Valletta.", "analyst.a");
        clock.Advance(TimeSpan.FromHours(1));
        c = s.Transition(c.Id, new TransitionCommand(CaseStatus.InReview), "analyst.a");
        c = s.AddDocument(c.Id, new AddDocumentCommand("Final", "Confirmed.", DocumentKind.Report), "analyst.a");
        c = s.Transition(c.Id, new TransitionCommand(CaseStatus.Closed, "Signed off"), "lead.b");
        Assert.Equal(CaseStatus.Closed, c.Status);
        c = s.Transition(c.Id, new TransitionCommand(CaseStatus.Open, "New evidence"), "lead.b");
        Assert.Equal(CaseStatus.Open, c.Status);

        Assert.Sequence(["created", "document-added", "status-changed", "document-added", "status-changed", "status-changed"], c.Audit.Select(a => a.Action));
        Assert.Equal("Open -> InReview (submit-for-review)", c.Audit[2].Detail);
        Assert.Equal("lead.b", c.Audit[4].Actor);
        Assert.Equal(clock.GetUtcNow(), c.UpdatedAt);
        Assert.Equal(6, c.Version);
    }

    [Test]
    public void Invalid_edges_are_rejected()
    {
        using var s = TestCorpus.EmptyService();
        var c = TestCorpus.NewCase(s, "x", "body");
        var ex = Assert.Throws<WorkflowViolationException>(() => s.Transition(c.Id, new TransitionCommand(CaseStatus.Closed, "skip"), "lead.b"));
        Assert.Equal("invalid-transition", ex.Rule);
        Assert.Throws<WorkflowViolationException>(() => s.Transition(c.Id, new TransitionCommand(CaseStatus.Open), "lead.b"));
    }

    [Test]
    public void Guards_enforce_documents_assignee_report_reason_and_four_eyes()
    {
        using var s = TestCorpus.EmptyService();
        var empty = s.Create(new CreateCaseCommand("no docs", Assignee: "analyst.a"), "analyst.a");
        Assert.Equal("submit-for-review", Assert.Throws<WorkflowViolationException>(() => s.Transition(empty.Id, new(CaseStatus.InReview), "analyst.a")).Rule);

        var unassigned = s.Create(new CreateCaseCommand("no assignee"), "analyst.a");
        s.AddDocument(unassigned.Id, new("n", "b"), "analyst.a");
        Assert.Throws<WorkflowViolationException>(() => s.Transition(unassigned.Id, new(CaseStatus.InReview), "analyst.a"));

        var c = TestCorpus.NewCase(s, "x", "body", "analyst.a");
        s.Transition(c.Id, new(CaseStatus.InReview), "analyst.a");
        var noReport = Assert.Throws<WorkflowViolationException>(() => s.Transition(c.Id, new(CaseStatus.Closed, "done"), "lead.b"));
        Assert.True(noReport.Message.Contains("Report", StringComparison.Ordinal));

        s.AddDocument(c.Id, new("r", "findings", DocumentKind.Report), "analyst.a");
        var fourEyes = Assert.Throws<WorkflowViolationException>(() => s.Transition(c.Id, new(CaseStatus.Closed, "done"), "ANALYST.A"));
        Assert.True(fourEyes.Message.Contains("Four-eyes", StringComparison.Ordinal));
        Assert.Throws<WorkflowViolationException>(() => s.Transition(c.Id, new(CaseStatus.Closed, "  "), "lead.b"));
        Assert.Equal(CaseStatus.Closed, s.Transition(c.Id, new(CaseStatus.Closed, "done"), "lead.b").Status);
    }

    [Test]
    public void Closed_cases_are_read_only_and_only_open_cases_can_be_deleted()
    {
        using var s = TestCorpus.EmptyService();
        var c = TestCorpus.NewCase(s, "x", "body", "analyst.a");
        s.Transition(c.Id, new(CaseStatus.InReview), "analyst.a");
        Assert.Equal("delete-only-open", Assert.Throws<WorkflowViolationException>(() => s.Delete(c.Id, "analyst.a")).Rule);
        s.AddDocument(c.Id, new("r", "findings", DocumentKind.Report), "analyst.a");
        s.Transition(c.Id, new(CaseStatus.Closed, "ok"), "lead.b");
        Assert.Equal("closed-is-read-only", Assert.Throws<WorkflowViolationException>(() => s.Update(c.Id, new(Title: "new"), "analyst.a")).Rule);
        Assert.Throws<WorkflowViolationException>(() => s.AddDocument(c.Id, new("n", "b"), "analyst.a"));
        Assert.Sequence([CaseStatus.Open], Workflow.AllowedTargets(CaseStatus.Closed));
    }

    [Test]
    public void Optimistic_concurrency_rejects_stale_versions()
    {
        using var s = TestCorpus.EmptyService();
        var c = TestCorpus.NewCase(s, "x", "body");
        s.Update(c.Id, new(Summary: "first", ExpectedVersion: c.Version), "analyst.a");
        Assert.Throws<ConcurrencyException>(() => s.Update(c.Id, new(Summary: "second", ExpectedVersion: c.Version), "analyst.b"));
    }
}
