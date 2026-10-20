using System;
using System.IO;

namespace Realm.Core.Logging;

/// <summary>Sink that writes lines such as <c>[Warn] Data.Loader: text</c> to a <see cref="TextWriter"/>.</summary>
/// <remarks>Thread-safety: writes are serialised with a lock.</remarks>
public sealed class TextWriterLogSink : ILogSink
{
    private readonly TextWriter _writer;
    private readonly LogLevel _minimum;
    private readonly object _gate = new();

    /// <summary>Creates a sink.</summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="minimum">Messages below this level are dropped.</param>
    public TextWriterLogSink(TextWriter writer, LogLevel minimum = LogLevel.Info)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _minimum = minimum;
    }

    /// <inheritdoc />
    public void Write(LogLevel level, string category, string message)
    {
        if (level < _minimum)
        {
            return;
        }

        lock (_gate)
        {
            _writer.WriteLine($"[{level}] {category}: {message}");
        }
    }
}
