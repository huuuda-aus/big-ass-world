namespace Realm.Core.Logging;

/// <summary>Severity of a log message, lowest to highest.</summary>
public enum LogLevel
{
    /// <summary>Detailed diagnostics.</summary>
    Debug = 0,

    /// <summary>Normal progress messages.</summary>
    Info = 1,

    /// <summary>Something unexpected that the program recovered from.</summary>
    Warn = 2,

    /// <summary>A failure.</summary>
    Error = 3,
}
