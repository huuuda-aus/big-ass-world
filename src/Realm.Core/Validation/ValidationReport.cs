using System;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Core.Validation;

/// <summary>Collects <see cref="ValidationIssue"/> values from one or more validators.</summary>
/// <remarks>
/// Determinism: issues keep insertion order; callers that need a stable order add in a stable order.
/// Thread-safety: not thread-safe.
/// </remarks>
public sealed class ValidationReport
{
    private readonly List<ValidationIssue> _issues = new();

    /// <summary>All issues in insertion order.</summary>
    public IReadOnlyList<ValidationIssue> Issues => _issues;

    /// <summary>True when at least one issue has <see cref="Severity.Error"/>.</summary>
    public bool HasErrors => _issues.Any(i => i.Severity == Severity.Error);

    /// <summary>Adds an issue.</summary>
    /// <param name="issue">The issue to add.</param>
    public void Add(ValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        _issues.Add(issue);
    }

    /// <summary>Adds an error.</summary>
    /// <param name="code">Stable issue code.</param>
    /// <param name="location">Where it was found.</param>
    /// <param name="message">What is wrong.</param>
    /// <param name="suggestedFix">Optional advice.</param>
    public void Error(string code, string location, string message, string? suggestedFix = null) =>
        Add(new ValidationIssue(Severity.Error, code, location, message, suggestedFix));

    /// <summary>Adds a warning.</summary>
    /// <param name="code">Stable issue code.</param>
    /// <param name="location">Where it was found.</param>
    /// <param name="message">What is suspicious.</param>
    /// <param name="suggestedFix">Optional advice.</param>
    public void Warning(string code, string location, string message, string? suggestedFix = null) =>
        Add(new ValidationIssue(Severity.Warning, code, location, message, suggestedFix));

    /// <summary>Counts issues of one severity.</summary>
    /// <param name="severity">Severity to count.</param>
    /// <returns>The number of matching issues.</returns>
    public int Count(Severity severity) => _issues.Count(i => i.Severity == severity);
}
