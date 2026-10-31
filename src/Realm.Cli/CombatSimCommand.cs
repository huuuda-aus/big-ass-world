using System;
using System.Globalization;
using System.IO;
using Realm.Sim.Combat;

namespace Realm.Cli;

internal static class CombatSimCommand
{
    private const int ExitOk = 0;
    private const int ExitOutOfBand = 1;
    private const int ExitUsage = 2;
    private const int DefaultFights = 1000;
    private const int DefaultMaxSeconds = 300;
    private const double Percent = 100.0;
    private const string Usage =
        "Usage: realm combat-sim [scenario] [--fights N] [--seed N] [--max-seconds S] [--min-win-rate F] [--max-win-rate F] [--list]";

    /// <summary>Runs one scenario many times and prints the win rate and fight lengths.</summary>
    /// <returns>0 on success, 1 when the win rate is outside the requested band, 2 on bad arguments.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        string scenario = "goblin-ambush";
        int fights = DefaultFights;
        ulong seed = 1;
        int maxSeconds = DefaultMaxSeconds;
        double minWinRate = 0, maxWinRate = 1;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--fights" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f > 0)
            {
                fights = f;
            }
            else if (arg == "--seed" && i + 1 < args.Length && ulong.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out ulong s))
            {
                seed = s;
            }
            else if (arg == "--max-seconds" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int m) && m > 0)
            {
                maxSeconds = m;
            }
            else if (arg == "--min-win-rate" && i + 1 < args.Length && TryParseRate(args[++i], out double lo))
            {
                minWinRate = lo;
            }
            else if (arg == "--max-win-rate" && i + 1 < args.Length && TryParseRate(args[++i], out double hi))
            {
                maxWinRate = hi;
            }
            else if (arg == "--list")
            {
                foreach ((string name, string description) in CombatScenarios.All)
                {
                    output.WriteLine($"{name,-16} {description}");
                }

                return ExitOk;
            }
            else if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                scenario = arg;
            }
            else
            {
                error.WriteLine($"Bad argument '{arg}'. {Usage}");
                return ExitUsage;
            }
        }

        if (CombatScenarios.Build(scenario, seed, 0) is null)
        {
            error.WriteLine($"Unknown scenario '{scenario}'. Run 'realm combat-sim --list'.");
            return ExitUsage;
        }

        int ticksPerSecond = CombatRules.Default.TicksPerSecond;
        CombatSimSummary summary = CombatSimRunner.Run(
            n => CombatScenarios.Build(scenario, seed, n)!, // the name was checked above
            fights,
            (long)maxSeconds * ticksPerSecond,
            CombatScenarios.HeroTeam);

        double Seconds(double ticks) => ticks / ticksPerSecond;
        output.WriteLine($"combat-sim {scenario}: {summary.Fights} fights, seed {seed}");
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  win rate      {summary.WinRate * Percent:0.0} % ({summary.Wins} won, {summary.Losses} lost, {summary.Timeouts} timed out)"));
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  fight length  mean {Seconds(summary.MeanTicks):0.0} s, median {Seconds(summary.MedianTicks):0.0} s, p90 {Seconds(summary.P90Ticks):0.0} s"));
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  health left   {summary.MeanHealthLeftPercent:0.0} % on average in a win"));

        if (summary.WinRate < minWinRate || summary.WinRate > maxWinRate)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Win rate {summary.WinRate:0.000} is outside the expected band {minWinRate:0.000} to {maxWinRate:0.000}."));
            return ExitOutOfBand;
        }

        return ExitOk;
    }

    private static bool TryParseRate(string text, out double rate) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out rate) && rate is >= 0 and <= 1;
}
