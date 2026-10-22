using System.Collections.Immutable;
using Casefile.Core.Entities;

namespace Casefile.Core.Domain;

public enum CaseStatus { Open, InReview, Closed }

public enum Priority { Low, Medium, High, Critical }

public enum DocumentKind { Note, Report }

/// <summary>
/// A case is an immutable value: every change produces a new instance through <c>with</c>.
/// Readers can hold a reference without locking, and the service swaps whole instances under its lock.
/// </summary>
public sealed record Case
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Summary { get; init; } = "";
    public CaseStatus Status { get; init; } = CaseStatus.Open;
    public Priority Priority { get; init; } = Priority.Medium;
    public string? Assignee { get; init; }
    public ImmutableList<string> Tags { get; init; } = [];
    public ImmutableList<CaseDocument> Documents { get; init; } = [];
    public ImmutableList<ExtractedEntity> Entities { get; init; } = [];
    public ImmutableList<AuditEntry> Audit { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int Version { get; init; } = 1;
}

public sealed record CaseDocument
{
    public required string Id { get; init; }
    public DocumentKind Kind { get; init; } = DocumentKind.Note;
    public required string Title { get; init; }
    public string Body { get; init; } = "";
    public string Author { get; init; } = "system";
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record AuditEntry(DateTimeOffset At, string Actor, string Action, string Detail);
