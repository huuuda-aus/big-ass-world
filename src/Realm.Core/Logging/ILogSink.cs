namespace Realm.Core.Logging;

/// <summary>Destination for log messages. Godot, the CLI and tests each plug in their own.</summary>
public interface ILogSink
{
    /// <summary>Receives one message.</summary>
    /// <param name="level">Message severity.</param>
    /// <param name="category">Category such as <c>Gen.Dungeon</c>.</param>
    /// <param name="message">Message text.</param>
    void Write(LogLevel level, string category, string message);
}
