using System;
using System.IO;
using Endfield.JsonTool.Core.Json;

var exitCode = Run(args);
Environment.Exit(exitCode);

static int Run(string[] args)
{
    if (args.Length == 0 || IsHelp(args[0]))
    {
        PrintHelp();
        return 0;
    }

    if (!args[0].Equals("decode", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Unknown command.");
        PrintHelp();
        return 2;
    }

    var input = GetOption(args, "-i", "--input");
    if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
    {
        Console.Error.WriteLine("Invalid input file.");
        return 2;
    }

    var output = GetOption(args, "-o", "--output");
    if (string.IsNullOrWhiteSpace(output))
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(input)) ?? Directory.GetCurrentDirectory();
        output = Path.Combine(dir, Path.GetFileNameWithoutExtension(input) + ".decrypted.json");
    }

    try
    {
        var encryptedBytes = File.ReadAllBytes(input);
        var firstStageBytes = JsonDecryptor.DecryptFirstStage(encryptedBytes);
        if (!JsonDecryptor.TryDecodeUtf8Json(firstStageBytes, out var plainJson))
        {
            Console.Error.WriteLine("Decrypt succeeded but content is not UTF-8 JSON text.");
            return 4;
        }

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        File.WriteAllText(output, plainJson);
        Console.WriteLine($"Decoded {Path.GetFileName(input)} -> {output}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Decode failed: {ex.Message}");
        return 3;
    }
}

static bool IsHelp(string arg) => arg is "-h" or "--help" or "help";

static string? GetOption(string[] args, string shortName, string longName)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(shortName, StringComparison.OrdinalIgnoreCase) ||
            args[i].Equals(longName, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}

static void PrintHelp()
{
    Console.WriteLine("Endfield.JsonTool CLI");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  endfield-json decode -i <input.json> [-o <output.json>]");
}
