using System;
using System.Collections.Generic;
using System.Globalization;

namespace Realm.Game.World;

/// <summary>Command-line options of the form <c>--name=value</c> (or <c>--name</c> alone), passed after <c>--</c> to Godot.</summary>
public sealed class LaunchArguments
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Parses raw arguments; anything not starting with <c>--</c> is ignored.</summary>
    /// <param name="args">The raw user arguments.</param>
    public LaunchArguments(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (string arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            int eq = arg.IndexOf('=', StringComparison.Ordinal);
            if (eq > 2)
            {
                _values[arg[2..eq]] = arg[(eq + 1)..];
            }
            else
            {
                _values[arg[2..]] = "true";
            }
        }
    }

    /// <summary>All parsed options, for reports.</summary>
    public IReadOnlyDictionary<string, string> All => _values;

    /// <summary>Whether an option was given.</summary>
    /// <param name="name">Option name without dashes.</param>
    /// <returns><c>true</c> when present.</returns>
    public bool Has(string name) => _values.ContainsKey(name);

    /// <summary>Reads a text option.</summary>
    /// <param name="name">Option name without dashes.</param>
    /// <param name="fallback">Value when absent.</param>
    /// <returns>The value.</returns>
    public string Text(string name, string fallback) => _values.TryGetValue(name, out string? value) ? value : fallback;

    /// <summary>Reads a number option (invariant culture).</summary>
    /// <param name="name">Option name without dashes.</param>
    /// <param name="fallback">Value when absent or not a number.</param>
    /// <returns>The value.</returns>
    public double Number(string name, double fallback) =>
        _values.TryGetValue(name, out string? text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v
            : fallback;

    /// <summary>Reads a yes/no option: <c>true</c> or <c>1</c> (or the bare flag) is yes.</summary>
    /// <param name="name">Option name without dashes.</param>
    /// <param name="fallback">Value when absent.</param>
    /// <returns>The value.</returns>
    public bool Flag(string name, bool fallback) => _values.TryGetValue(name, out string? text) ? text is "true" or "1" : fallback;
}
