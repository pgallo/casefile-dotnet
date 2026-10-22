namespace Casefile.Core.Domain;

public abstract class CasefileException(string message) : Exception(message);

public sealed class NotFoundException(string what) : CasefileException($"{what} not found");

public sealed class ValidationException(string message) : CasefileException(message);

/// <summary>A workflow rule was violated. <see cref="Rule"/> is a stable code clients can switch on.</summary>
public sealed class WorkflowViolationException(string rule, string message) : CasefileException(message)
{
    public string Rule { get; } = rule;
}
