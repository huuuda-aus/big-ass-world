namespace Realm.Core.Validation;

/// <summary>One finding from a validator (CLAUDE.md section 7).</summary>
/// <param name="Severity">How serious the finding is.</param>
/// <param name="Code">Stable code such as <c>DATA002</c>, documented in docs/reference/validators.md.</param>
/// <param name="Location">Where it was found, for example <c>content/materials/iron_ore.yaml:3</c>.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="SuggestedFix">How to fix it, or null when there is no general advice.</param>
public sealed record ValidationIssue(
    Severity Severity,
    string Code,
    string Location,
    string Message,
    string? SuggestedFix = null)
{
    /// <summary>Formats as <c>Severity Code Location: Message (fix: ...)</c>.</summary>
    /// <returns>A single-line description.</returns>
    public override string ToString() =>
        $"{Severity} {Code} {Location}: {Message}" + (SuggestedFix is null ? string.Empty : $" (fix: {SuggestedFix})");
}
