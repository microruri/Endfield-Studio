using System;
using System.Collections.Generic;
using System.IO;
using Endfield.BlcTool.Core.Blc;
using Endfield.JsonTool.Core.Json;

var exitCode = Run(args);
Environment.Exit(exitCode);

static int Run(string[] args)
{
    if (args.Length == 0 || HasFlag(args, "-h", "--help"))
    {
        PrintUsage();
        return 0;
    }

    var gameRoot = GetOption(args, "-g", "--game-root");
    var operation = GetOption(args, "-t", "--type");
    var outputPath = GetOption(args, "-o", "--output");

    if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(operation) || string.IsNullOrWhiteSpace(outputPath))
    {
        Console.Error.WriteLine("Missing required options.");
        PrintUsage();
        return 2;
    }

    if (!Directory.Exists(gameRoot))
    {
        Console.Error.WriteLine($"Game root directory does not exist: {gameRoot}");
        return 2;
    }

    try
    {
        return operation.ToLowerInvariant() switch
        {
            "blc-all" => ConvertAllBlc(gameRoot, outputPath),
            "json-index" => ConvertIndexJson(gameRoot, outputPath),
            _ => UnknownOperation(operation)
        };
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"Operation failed: {e.Message}");
        return 1;
    }
}

static bool HasFlag(string[] args, string shortName, string longName)
{
    foreach (var arg in args)
    {
        if (arg.Equals(shortName, StringComparison.OrdinalIgnoreCase) || arg.Equals(longName, StringComparison.OrdinalIgnoreCase))
            return true;
    }

    return false;
}

static string? GetOption(string[] args, string shortName, string longName)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(shortName, StringComparison.OrdinalIgnoreCase) || args[i].Equals(longName, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}

static int ConvertAllBlc(string gameRoot, string outputPath)
{
    var persistentVfsRoot = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "VFS");
    var streamingVfsRoot = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "VFS");

    var hasPersistent = Directory.Exists(persistentVfsRoot);
    var hasStreaming = Directory.Exists(streamingVfsRoot);

    if (!hasPersistent && !hasStreaming)
    {
        Console.Error.WriteLine($"VFS directories not found. Checked:\n- {persistentVfsRoot}\n- {streamingVfsRoot}");
        return 3;
    }

    Directory.CreateDirectory(outputPath);

    var ok = 0;
    var fail = 0;
    var skippedStreamingDuplicates = 0;

    var processedBlcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (hasPersistent)
    {
        var persistentFiles = Directory.GetFiles(persistentVfsRoot, "*.blc", SearchOption.AllDirectories);
        Console.WriteLine($"Persistent: found {persistentFiles.Length} .blc file(s).");

        foreach (var blc in persistentFiles)
        {
            var blcName = Path.GetFileName(blc);
            ProcessBlcFile(blc, persistentVfsRoot, outputPath, ref ok, ref fail);
            processedBlcNames.Add(blcName);
        }
    }
    else
    {
        Console.WriteLine("Persistent: VFS not found, skipping.");
    }

    if (hasStreaming)
    {
        var streamingFiles = Directory.GetFiles(streamingVfsRoot, "*.blc", SearchOption.AllDirectories);
        Console.WriteLine($"StreamingAssets: found {streamingFiles.Length} .blc file(s).");

        foreach (var blc in streamingFiles)
        {
            var blcName = Path.GetFileName(blc);
            if (processedBlcNames.Contains(blcName))
            {
                skippedStreamingDuplicates++;
                var relative = Path.GetRelativePath(streamingVfsRoot, blc);
                Console.WriteLine($"[SKIP] {relative} (duplicate name already handled from Persistent)");
                continue;
            }

            ProcessBlcFile(blc, streamingVfsRoot, outputPath, ref ok, ref fail);
            processedBlcNames.Add(blcName);
        }
    }
    else
    {
        Console.WriteLine("StreamingAssets: VFS not found, skipping.");
    }

    Console.WriteLine($"Done. Success={ok}, Failed={fail}, SkippedStreamingDuplicates={skippedStreamingDuplicates}");
    return fail == 0 ? 0 : 1;
}

static void ProcessBlcFile(string blcPath, string vfsRoot, string outputPath, ref int ok, ref int fail)
{
    try
    {
        var relative = Path.GetRelativePath(vfsRoot, blcPath);
        var outputFile = Path.Combine(outputPath, Path.ChangeExtension(relative, ".json"));
        var outputDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var bytes = File.ReadAllBytes(blcPath);
        var parsed = BlcDecoder.Decode(bytes);
        var json = System.Text.Json.JsonSerializer.Serialize(parsed, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(outputFile, json);

        ok++;
        Console.WriteLine($"[OK] {relative} -> {Path.GetRelativePath(outputPath, outputFile)}");
    }
    catch (Exception ex)
    {
        fail++;
        Console.Error.WriteLine($"[FAIL] {blcPath}: {ex.Message}");
    }
}

static int UnknownOperation(string operation)
{
    Console.Error.WriteLine($"Unknown operation: {operation}");
    Console.Error.WriteLine("Supported operations: blc-all, json-index");
    return 2;
}

static int ConvertIndexJson(string gameRoot, string outputPath)
{
    var candidates = new[]
    {
        (Input: Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "index_main.json"), Output: "StreamingAssets.index_main.json"),
        (Input: Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "index_initial.json"), Output: "StreamingAssets.index_initial.json"),
        (Input: Path.Combine(gameRoot, "Endfield_Data", "Persistent", "index_main.json"), Output: "Persistent.index_main.json"),
        (Input: Path.Combine(gameRoot, "Endfield_Data", "Persistent", "index_initial.json"), Output: "Persistent.index_initial.json")
    };

    Directory.CreateDirectory(outputPath);

    var found = 0;
    var ok = 0;
    var fail = 0;
    var partial = 0;

    foreach (var item in candidates)
    {
        if (!File.Exists(item.Input))
            continue;

        found++;
        try
        {
            var encryptedBytes = File.ReadAllBytes(item.Input);
            var firstStageBytes = JsonDecryptor.DecryptFirstStage(encryptedBytes);

            var outFile = Path.Combine(outputPath, item.Output);

            if (JsonDecryptor.TryDecodeUtf8Json(firstStageBytes, out var plainJson))
            {
                File.WriteAllText(outFile, plainJson);
                ok++;
                Console.WriteLine($"[OK] {item.Input} -> {outFile}");
            }
            else
            {
                var binFile = Path.Combine(outputPath, Path.ChangeExtension(item.Output, ".stage1.bin"));
                var previewFile = Path.Combine(outputPath, Path.ChangeExtension(item.Output, ".stage1.preview.txt"));

                File.WriteAllBytes(binFile, firstStageBytes);
                File.WriteAllText(previewFile, JsonDecryptor.BuildPrintablePreview(firstStageBytes));
                partial++;
                ok++;
                Console.WriteLine($"[PARTIAL] {item.Input}: stage-1 decrypt succeeded (not UTF-8 JSON). Saved: {binFile}, {previewFile}");
            }
        }
        catch (Exception ex)
        {
            fail++;
            Console.Error.WriteLine($"[FAIL] {item.Input}: {ex.Message}");
        }
    }

    if (found == 0)
    {
        Console.Error.WriteLine("No index json files found under Endfield_Data/StreamingAssets or Endfield_Data/Persistent.");
        return 3;
    }

    Console.WriteLine($"Done. Success={ok}, Partial={partial}, Failed={fail}");
    return fail == 0 ? 0 : 1;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Endfield.Cli -g <gameRoot> -t <operation> -o <outputPath>");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -g, --game-root   Game root directory.");
    Console.WriteLine("  -t, --type        Operation type. Supported: blc-all, json-index");
    Console.WriteLine("  -o, --output      Output directory.");
    Console.WriteLine("  -h, --help        Show help.");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t blc-all -o \"C:\\temp\\endfield-json\"");
}
