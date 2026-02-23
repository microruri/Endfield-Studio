using System;
using System.IO;
using Endfield.Tool.CLI.Operations;

namespace Endfield.Tool.CLI.App;

/// <summary>
/// Main command router and high-level validation for Endfield.Tool.CLI.
/// </summary>
public static class CliApplication
{
    /// <summary>
    /// Entry method for command routing and high-level validation.
    /// </summary>
    public static int Run(string[] args)
    {
        var parsed = CliArguments.Parse(args);
        if (parsed.ShowHelp)
        {
            CliUsage.Print();
            return 0;
        }

        if (string.IsNullOrWhiteSpace(parsed.GameRoot) || string.IsNullOrWhiteSpace(parsed.Operation))
        {
            Console.Error.WriteLine("Missing required options: -g and -t are required.");
            CliUsage.Print();
            return 2;
        }

        if (!Directory.Exists(parsed.GameRoot))
        {
            Console.Error.WriteLine($"Game root directory does not exist: {parsed.GameRoot}");
            return 2;
        }

        try
        {
            return parsed.Operation.ToLowerInvariant() switch
            {
                "extract" => string.IsNullOrWhiteSpace(parsed.ResourceTypeName)
                    ? MissingOptionForOperation("extract", "-n/--name")
                    : string.IsNullOrWhiteSpace(parsed.OutputPath)
                        ? MissingOptionForOperation("extract", "-o/--output")
                        : ExtractOperation.Execute(parsed.GameRoot, parsed.ResourceTypeName, parsed.OutputPath, parsed.DecodeContent),

                _ => UnknownOperation(parsed.Operation)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Operation failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Prints a standardized missing-option message.
    /// </summary>
    private static int MissingOptionForOperation(string operation, string option)
    {
        Console.Error.WriteLine($"Operation '{operation}' requires option {option}.");
        return 2;
    }

    /// <summary>
    /// Prints unsupported operation message.
    /// </summary>
    private static int UnknownOperation(string operation)
    {
        Console.Error.WriteLine($"Unknown operation: {operation}");
        Console.Error.WriteLine("Supported operations: extract");
        return 2;
    }
}
