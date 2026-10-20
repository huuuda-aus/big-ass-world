namespace Realm.Core.Validation;

/// <summary>How serious a <see cref="ValidationIssue"/> is.</summary>
public enum Severity
{
    /// <summary>Informational note; never fails a run.</summary>
    Info = 0,

    /// <summary>Suspicious but usable; does not fail a run by default.</summary>
    Warning = 1,

    /// <summary>Invalid; fails the run.</summary>
    Error = 2,
}
