using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Endfield.BlcTool.Core.Blc;

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
    if (string.IsNullOrWhiteSpace(input) || !File.Exists(input) || !input.EndsWith(".blc", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Invalid input .blc file.");
        return 2;
    }

    var output = GetOption(args, "-o", "--output");
    if (string.IsNullOrWhiteSpace(output))
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(input)) ?? Directory.GetCurrentDirectory();
        output = Path.Combine(dir, Path.GetFileNameWithoutExtension(input) + ".json");
    }

    var verbose = args.Any(x => x.Equals("-v", StringComparison.OrdinalIgnoreCase) || x.Equals("--verbose", StringComparison.OrdinalIgnoreCase));

    try
    {
        var bytes = File.ReadAllBytes(input);
        var info = BlcDecoder.Decode(bytes);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var json = JsonSerializer.Serialize(info, jsonOptions);
        File.WriteAllText(output, json);

        if (verbose)
        {
            var fileCount = info.AllChunks.Sum(c => c.Files.Count);
            Console.WriteLine($"Version: {info.Version}");
            Console.WriteLine($"Chunks: {info.AllChunks.Count}");
            Console.WriteLine($"Files: {fileCount}");
        }

        Console.WriteLine($"Decoded {Path.GetFileName(input)} -> {output}");
        return 0;
    }
    catch (InvalidDataException e)
    {
        Console.Error.WriteLine($"Parse failed: {e.Message}");
        return 4;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"Decode failed: {e.Message}");
        return 3;
    }
}

static bool IsHelp(string arg) => arg is "-h" or "--help" or "help";

static string? GetOption(string[] args, string shortName, string longName)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(shortName, StringComparison.OrdinalIgnoreCase) || args[i].Equals(longName, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}

static void PrintHelp()
{
    Console.WriteLine("Endfield.BlcTool CLI");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  endfield-blc decode -i <input.blc> [-o <output.json>] [-v]");
}
