using System;
using System.Collections.Generic;
using System.IO;
using Realm.Core.Logging;
using Xunit;

namespace Realm.Core.Tests;

// Log.Sink is process-wide. Tests in one class run serially and always restore the sink.
public class LoggingTests
{
    private sealed class CaptureSink : ILogSink
    {
        public List<(LogLevel Level, string Category, string Message)> Entries { get; } = new();

        public void Write(LogLevel level, string category, string message) => Entries.Add((level, category, message));
    }

    [Fact]
    public void Logger_RoutesLevelCategoryAndMessageToSink()
    {
        var sink = new CaptureSink();
        Log.Sink = sink;
        try
        {
            Logger log = Log.For("Gen.Dungeon");
            log.Debug("d");
            log.Info("i");
            log.Warn("w");
            log.Error("e");
        }
        finally
        {
            Log.Sink = null!;
        }

        Assert.Equal(
            new[]
            {
                (LogLevel.Debug, "Gen.Dungeon", "d"),
                (LogLevel.Info, "Gen.Dungeon", "i"),
                (LogLevel.Warn, "Gen.Dungeon", "w"),
                (LogLevel.Error, "Gen.Dungeon", "e"),
            },
            sink.Entries);
    }

    [Fact]
    public void DefaultSink_DiscardsWithoutThrowing()
    {
        Log.Sink = null!;
        Log.For("Any").Error("nobody listens");

        Assert.IsType<NullLogSink>(Log.Sink);
    }

    [Fact]
    public void For_RejectsEmptyCategory()
    {
        Assert.Throws<ArgumentException>(() => Log.For(string.Empty));
        Assert.Throws<ArgumentNullException>(() => Log.For(null!));
    }

    [Fact]
    public void TextWriterSink_FormatsLines_AndFiltersBelowMinimum()
    {
        var writer = new StringWriter();
        var sink = new TextWriterLogSink(writer, LogLevel.Warn);

        sink.Write(LogLevel.Info, "Cat", "hidden");
        sink.Write(LogLevel.Warn, "Data.Loader", "shown");

        Assert.Equal("[Warn] Data.Loader: shown" + writer.NewLine, writer.ToString());
    }
}
