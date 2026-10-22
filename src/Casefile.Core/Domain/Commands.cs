namespace Casefile.Core.Domain;

public sealed record CreateCaseCommand(
    string Title,
    string? Summary = null,
    Priority Priority = Priority.Medium,
    string? Assignee = null,
    IReadOnlyList<string>? Tags = null);

/// <summary>Partial update: null fields are left unchanged. <see cref="ExpectedVersion"/> enables optimistic concurrency.</summary>
public sealed record UpdateCaseCommand(
    string? Title = null,
    string? Summary = null,
    Priority? Priority = null,
    string? Assignee = null,
    IReadOnlyList<string>? Tags = null,
    int? ExpectedVersion = null);

public sealed record AddDocumentCommand(string Title, string Body, DocumentKind Kind = DocumentKind.Note);

public sealed record TransitionCommand(CaseStatus To, string? Reason = null);

public sealed class ConcurrencyException(string caseId, int expected, int actual)
    : CasefileException($"Case {caseId} is at version {actual}, expected {expected}.");
