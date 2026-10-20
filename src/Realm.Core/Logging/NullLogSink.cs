namespace Realm.Core.Logging;

/// <summary>Sink that discards everything. The default.</summary>
public sealed class NullLogSink : ILogSink
{
    /// <summary>The shared instance.</summary>
    public static readonly NullLogSink Instance = new();

    private NullLogSink()
    {
    }

    /// <inheritdoc />
    public void Write(LogLevel level, string category, string message)
    {
    }
}
