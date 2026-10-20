using System;
using System.Threading;

namespace Realm.Core.Logging;

/// <summary>
/// Entry point of the logging facade (CLAUDE.md section 8.4). This is the one documented service
/// locator in Core: the process-wide sink is replaceable so hosts can route output.
/// </summary>
/// <remarks>
/// Thread-safety: <see cref="Sink"/> reads and writes are atomic; sinks must be thread-safe themselves.
/// Logging never affects simulation results.
/// </remarks>
public static class Log
{
    private static ILogSink _sink = NullLogSink.Instance;

    /// <summary>The active sink. Setting null restores the default sink that discards messages.</summary>
    public static ILogSink Sink
    {
        get => Volatile.Read(ref _sink);
        set => Volatile.Write(ref _sink, value ?? NullLogSink.Instance);
    }

    /// <summary>Creates a logger bound to a category.</summary>
    /// <param name="category">Category such as <c>Gen.Dungeon</c>.</param>
    /// <returns>The logger.</returns>
    /// <exception cref="ArgumentException"><paramref name="category"/> is null or empty.</exception>
    public static Logger For(string category)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);
        return new Logger(category);
    }
}
