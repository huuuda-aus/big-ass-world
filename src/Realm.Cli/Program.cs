using System;
using System.Reflection;
using Realm.Cli;
using Realm.Core.Logging;

const int ExitOk = 0;
const int ExitUsage = 2;

Log.Sink = new TextWriterLogSink(Console.Error, LogLevel.Info);

string command = args.Length > 0 ? args[0] : "help";

switch (command)
{
    case "version":
        Console.WriteLine($"Realm.Cli {Assembly.GetExecutingAssembly().GetName().Version}");
        return ExitOk;
    case "content-lint":
        return ContentLintCommand.Run(args.Length > 1 ? args[1] : "content", Console.Out);
    case "macro-map":
        return MacroMapCommand.Run(args.Length > 1 ? args[1] : "content", Console.Out);
    case "height-field":
        return HeightFieldCommand.Run(args, Console.Out, Console.Error);
    case "bake-terrain":
        return BakeTerrainCommand.Run(args, Console.Out, Console.Error);
    case "help":
        Console.WriteLine("Usage: realm <command>");
        Console.WriteLine("Commands: version, bake-terrain [dir] [--out dir] [--seed N] [--spacing M] [--region-size N] [--no-erode], content-lint [dir], macro-map [dir], height-field [dir] [--seed N] [--spacing M] [--blend M] [--erode] [--iterations N] [--strength S] [--threshold KM2] [--min-lake-area KM2] [--png file], help");
        return ExitOk;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Try 'realm help'.");
        return ExitUsage;
}
