namespace Realm.Core.Logging;

/// <summary>A logger bound to one category. Create with <see cref="Log.For"/>.</summary>
public readonly struct Logger
{
    internal Logger(string category) => Category = category;

    /// <summary>The category this logger writes under.</summary>
    public string Category { get; }

    /// <summary>Writes a debug message.</summary>
    /// <param name="message">Message text.</param>
    public void Debug(string message) => Log.Sink.Write(LogLevel.Debug, Category, message);

    /// <summary>Writes an info message.</summary>
    /// <param name="message">Message text.</param>
    public void Info(string message) => Log.Sink.Write(LogLevel.Info, Category, message);

    /// <summary>Writes a warning.</summary>
    /// <param name="message">Message text.</param>
    public void Warn(string message) => Log.Sink.Write(LogLevel.Warn, Category, message);

    /// <summary>Writes an error.</summary>
    /// <param name="message">Message text.</param>
    public void Error(string message) => Log.Sink.Write(LogLevel.Error, Category, message);
}
