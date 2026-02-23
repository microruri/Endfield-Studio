using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Endfield.BlcTool.Core.Blc;
using Endfield.Cli.App;

namespace Endfield.Cli.Operations;

/// <summary>
/// Converts all discovered .blc files to formatted JSON grouped by GroupCfgName.
/// </summary>
public static class BlcAllOperation
{
    public static int Execute(string gameRoot, string outputPath)
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
        Directory.CreateDirectory(Path.Combine(outputPath, "blc_groups"));

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

    private static void ProcessBlcFile(string blcPath, string vfsRoot, string outputPath, ref int ok, ref int fail)
    {
        try
        {
            var relative = Path.GetRelativePath(vfsRoot, blcPath);
            var bytes = File.ReadAllBytes(blcPath);
            var parsed = BlcDecoder.Decode(bytes);

            var groupName = CliHelpers.SanitizeFileName(parsed.GroupCfgName);
            if (string.IsNullOrWhiteSpace(groupName))
                groupName = Path.GetFileNameWithoutExtension(blcPath);

            var outputFile = Path.Combine(outputPath, "blc_groups", $"{groupName}.json");
            var outputDir = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);

            var json = JsonSerializer.Serialize(parsed, new JsonSerializerOptions
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
}
