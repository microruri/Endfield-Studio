using System;

namespace Endfield.Tool.CLI.App;

/// <summary>
/// Parsed command line options for Endfield.Tool.CLI.
/// </summary>
public sealed class CliArguments
{
    /// <summary>
    /// Whether help output should be shown.
    /// </summary>
    public bool ShowHelp { get; }

    /// <summary>
    /// Game root directory.
    /// </summary>
    public string? GameRoot { get; }

    /// <summary>
    /// Operation name from command line.
    /// </summary>
    public string? Operation { get; }

    /// <summary>
    /// Resource type name for extract operation.
    /// </summary>
    public string? ResourceTypeName { get; }

    /// <summary>
    /// Output directory path.
    /// </summary>
    public string? OutputPath { get; }

    private CliArguments(bool showHelp, string? gameRoot, string? operation, string? resourceTypeName, string? outputPath)
    {
        ShowHelp = showHelp;
        GameRoot = gameRoot;
        Operation = operation;
        ResourceTypeName = resourceTypeName;
        OutputPath = outputPath;
    }

    /// <summary>
    /// Parses known options from raw args.
    /// </summary>
    public static CliArguments Parse(string[] args)
    {
        var showHelp = args.Length == 0 || HasFlag(args, "-h", "--help");
        var gameRoot = GetOption(args, "-g", "--game-root");
        var operation = GetOption(args, "-t", "--type");
        var resourceTypeName = GetOption(args, "-n", "--name");
        var outputPath = GetOption(args, "-o", "--output");

        return new CliArguments(showHelp, gameRoot, operation, resourceTypeName, outputPath);
    }

    /// <summary>
    /// Checks whether one of two flag names exists.
    /// </summary>
    private static bool HasFlag(string[] args, string shortName, string longName)
    {
        foreach (var arg in args)
        {
            if (arg.Equals(shortName, StringComparison.OrdinalIgnoreCase) ||
                arg.Equals(longName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads an option value from short or long option name.
    /// </summary>
    private static string? GetOption(string[] args, string shortName, string longName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(shortName, StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals(longName, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
