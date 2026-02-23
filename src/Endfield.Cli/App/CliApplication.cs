using System;
using System.IO;
using Endfield.Cli.Operations;

namespace Endfield.Cli.App;

/// <summary>
/// Main command router and high-level validation for Endfield.Cli.
/// </summary>
public static class CliApplication
{
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
                "blc-all" => string.IsNullOrWhiteSpace(parsed.OutputPath)
                    ? MissingOptionForOperation("blc-all", "-o/--output")
                    : BlcAllOperation.Execute(parsed.GameRoot, parsed.OutputPath),

                "json-index" => string.IsNullOrWhiteSpace(parsed.OutputPath)
                    ? MissingOptionForOperation("json-index", "-o/--output")
                    : JsonIndexOperation.Execute(parsed.GameRoot, parsed.OutputPath),

                "chk-list" => string.IsNullOrWhiteSpace(parsed.ResourceTypeName)
                    ? MissingOptionForOperation("chk-list", "-n/--name")
                    : ChkListOperation.Execute(parsed.GameRoot, parsed.ResourceTypeName),

                "extract-type" => string.IsNullOrWhiteSpace(parsed.ResourceTypeName)
                    ? MissingOptionForOperation("extract-type", "-n/--name")
                    : string.IsNullOrWhiteSpace(parsed.OutputPath)
                        ? MissingOptionForOperation("extract-type", "-o/--output")
                        : ExtractTypeOperation.Execute(parsed.GameRoot, parsed.ResourceTypeName, parsed.OutputPath, parsed.DecodeContent),

                "manifest-assets-yaml" => string.IsNullOrWhiteSpace(parsed.OutputPath)
                    ? MissingOptionForOperation("manifest-assets-yaml", "-o/--output")
                    : ManifestAssetsYamlOperation.Execute(parsed.GameRoot, parsed.OutputPath),

                _ => UnknownOperation(parsed.Operation)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Operation failed: {ex.Message}");
            return 1;
        }
    }

    private static int MissingOptionForOperation(string operation, string option)
    {
        Console.Error.WriteLine($"Operation '{operation}' requires option {option}.");
        return 2;
    }

    private static int UnknownOperation(string operation)
    {
        Console.Error.WriteLine($"Unknown operation: {operation}");
        Console.Error.WriteLine("Supported operations: blc-all, json-index, chk-list, extract-type, manifest-assets-yaml");
        return 2;
    }
}
