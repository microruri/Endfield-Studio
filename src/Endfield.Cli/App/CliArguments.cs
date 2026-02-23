using System;

namespace Endfield.Cli.App;

/// <summary>
/// Parsed command line options for Endfield.Cli.
/// </summary>
public sealed class CliArguments
{
    public string[] RawArgs { get; }
    public bool ShowHelp { get; }
    public bool DecodeContent { get; }
    public string? GameRoot { get; }
    public string? Operation { get; }
    public string? OutputPath { get; }
    public string? ResourceTypeName { get; }
    public string? ChkPath { get; }
    public string? FileNameFilter { get; }

    private CliArguments(string[] rawArgs, bool showHelp, bool decodeContent, string? gameRoot, string? operation, string? outputPath, string? resourceTypeName, string? chkPath, string? fileNameFilter)
    {
        RawArgs = rawArgs;
        ShowHelp = showHelp;
        DecodeContent = decodeContent;
        GameRoot = gameRoot;
        Operation = operation;
        OutputPath = outputPath;
        ResourceTypeName = resourceTypeName;
        ChkPath = chkPath;
        FileNameFilter = fileNameFilter;
    }

    /// <summary>
    /// Parses known options from raw args.
    /// </summary>
    public static CliArguments Parse(string[] args)
    {
        var showHelp = args.Length == 0 || HasFlag(args, "-h", "--help");
        var decodeContent = HasFlag(args, "-d", "--decode-content");
        var gameRoot = GetOption(args, "-g", "--game-root");
        var operation = GetOption(args, "-t", "--type");
        var outputPath = GetOption(args, "-o", "--output");
        var resourceTypeName = GetOption(args, "-n", "--name");
        var chkPath = GetOption(args, "-c", "--chk");
        var fileNameFilter = GetOption(args, "-f", "--filter");

        return new CliArguments(args, showHelp, decodeContent, gameRoot, operation, outputPath, resourceTypeName, chkPath, fileNameFilter);
    }

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
