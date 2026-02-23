using System;
using System.Collections.Generic;
using System.IO;
using Endfield.Cli.App;
using Endfield.Cli.Extract;

namespace Endfield.Cli.Operations;

/// <summary>
/// Decodes Endfield VFS-wrapped .ab files into plain UnityFS bundle bytes.
/// Prints detailed container info and writes fully decrypted payload outputs.
/// </summary>
public static class DecodeAbOperation
{
    public static int Execute(string inputAbPath, string outputPath)
    {
        var fullInput = Path.GetFullPath(inputAbPath);
        if (!File.Exists(fullInput))
        {
            Console.Error.WriteLine($"[FAIL] Input .ab not found: {fullInput}");
            return 2;
        }

        Console.WriteLine("[STEP 1/4] Read input .ab and decode Endfield VFS wrapper...");
        var input = File.ReadAllBytes(fullInput);
        EndfieldVfsDecodeResult result;
        try
        {
            result = EndfieldVfsDecoder.DecodeToUnityFs(input);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FAIL] Decode failed: {ex.Message}");
            return 3;
        }

        Console.WriteLine("[STEP 2/4] Print decoded container details...");
        Console.WriteLine($"[INFO] InputPath={fullInput}");
        Console.WriteLine($"[INFO] InputSize={input.Length}");
        Console.WriteLine($"[INFO] Input.SignaturePreview={ExtractAsciiPrefix(input, 8)}");
        Console.WriteLine($"[INFO] Header.Size={result.Header.Size}");
        Console.WriteLine($"[INFO] Header.Flags=0x{result.Header.Flags:X8}");
        Console.WriteLine($"[INFO] Header.EncFlags=0x{result.Header.EncFlags:X8}");
        Console.WriteLine($"[INFO] Header.CompressedBlocksInfoSize={result.Header.CompressedBlocksInfoSize}");
        Console.WriteLine($"[INFO] Header.UncompressedBlocksInfoSize={result.Header.UncompressedBlocksInfoSize}");
        Console.WriteLine($"[INFO] Blocks.UncompressedStreamSize={result.BlocksUncompressedBytes.Length}");
        Console.WriteLine($"[INFO] Blocks.Count={result.Blocks.Count}");

        long totalCompressed = 0;
        long totalUncompressed = 0;
        for (var i = 0; i < result.Blocks.Count; i++)
        {
            var b = result.Blocks[i];
            totalCompressed += b.CompressedSize;
            totalUncompressed += b.UncompressedSize;
            Console.WriteLine($"[BLOCK] index={i}, compressed={b.CompressedSize}, uncompressed={b.UncompressedSize}, flags=0x{b.Flags:X4}, compType={b.CompressionType}");
        }

        Console.WriteLine($"[INFO] Blocks.TotalCompressed={totalCompressed} ({FormatBytes(totalCompressed)})");
        Console.WriteLine($"[INFO] Blocks.TotalUncompressed={totalUncompressed} ({FormatBytes(totalUncompressed)})");

        Console.WriteLine($"[INFO] Nodes.Count={result.Nodes.Count}");
        for (var i = 0; i < result.Nodes.Count; i++)
        {
            var n = result.Nodes[i];
            var valid = n.Offset >= 0 && n.Size > 0 && n.Offset + n.Size <= result.BlocksUncompressedBytes.LongLength;
            Console.WriteLine($"[NODE] index={i}, path={n.Path}, offset={n.Offset}, size={n.Size}, flags=0x{n.Flags:X8}, valid={valid}");
        }

        var selectedNodeIndex = FindSelectedNodeIndex(result.Nodes, result.SelectedNode);
        var unityFsDetected = StartsWithAscii(result.UnityFsBytes, "UnityFS");
        Console.WriteLine($"[INFO] SelectedNode.Index={selectedNodeIndex}");
        Console.WriteLine($"[INFO] SelectedNode.Path={result.SelectedNode.Path}");
        Console.WriteLine($"[INFO] UnityFS.Detected={unityFsDetected}");
        Console.WriteLine($"[INFO] UnityFS.SignaturePreview={result.UnityFsSignature}");
        Console.WriteLine($"[INFO] UnityFS.Size={result.UnityFsBytes.Length}");

        Console.WriteLine("[STEP 3/4] Write fully decrypted outputs...");
        var unityFsOutput = ResolveUnityFsOutputPath(outputPath, fullInput);
        var fullOutputRoot = ResolveOutputRoot(outputPath, fullInput, unityFsOutput);
        Directory.CreateDirectory(fullOutputRoot);

        var unityFsOutDir = Path.GetDirectoryName(unityFsOutput);
        if (!string.IsNullOrWhiteSpace(unityFsOutDir))
            Directory.CreateDirectory(unityFsOutDir);

        File.WriteAllBytes(unityFsOutput, result.UnityFsBytes);
        Console.WriteLine($"[OK] UnityFS written: {unityFsOutput}");

        var blocksOutput = Path.Combine(fullOutputRoot, "blocks_uncompressed.bin");
        File.WriteAllBytes(blocksOutput, result.BlocksUncompressedBytes);
        Console.WriteLine($"[OK] Decrypted blocks stream written: {blocksOutput}");

        var extracted = ExtractAllNodes(result, fullOutputRoot);
        Console.WriteLine($"[INFO] NodeExtract.Success={extracted.Success}, NodeExtract.Skipped={extracted.Skipped}, NodeExtract.Failed={extracted.Failed}");

        Console.WriteLine($"[INFO] OutputRoot={fullOutputRoot}");

        Console.WriteLine("[STEP 4/4] Done.");
        return extracted.Failed == 0 ? 0 : 1;
    }

    private static string ResolveUnityFsOutputPath(string outputPath, string inputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return Path.ChangeExtension(inputPath, ".unityfs");

        if (outputPath.EndsWith(Path.DirectorySeparatorChar) ||
            outputPath.EndsWith(Path.AltDirectorySeparatorChar) ||
            string.IsNullOrWhiteSpace(Path.GetExtension(outputPath)))
        {
            var inputName = Path.GetFileNameWithoutExtension(inputPath);
            return Path.Combine(outputPath, inputName + ".unityfs");
        }

        return outputPath;
    }

    private static string ResolveOutputRoot(string outputPath, string inputPath, string unityFsOutput)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            var inputDir = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
            var inputName = Path.GetFileNameWithoutExtension(inputPath);
            return Path.Combine(inputDir, inputName + "_decoded");
        }

        if (outputPath.EndsWith(Path.DirectorySeparatorChar) ||
            outputPath.EndsWith(Path.AltDirectorySeparatorChar) ||
            string.IsNullOrWhiteSpace(Path.GetExtension(outputPath)))
            return outputPath;

        var unityDir = Path.GetDirectoryName(unityFsOutput) ?? Directory.GetCurrentDirectory();
        var unityName = Path.GetFileNameWithoutExtension(unityFsOutput);
        return Path.Combine(unityDir, unityName + "_decoded");
    }

    private static NodeExtractSummary ExtractAllNodes(EndfieldVfsDecodeResult result, string outputRoot)
    {
        var summary = new NodeExtractSummary();
        var nodesRoot = Path.Combine(outputRoot, "nodes");
        Directory.CreateDirectory(nodesRoot);

        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < result.Nodes.Count; i++)
        {
            var node = result.Nodes[i];
            if (node.Offset < 0 || node.Size <= 0 || node.Offset + node.Size > result.BlocksUncompressedBytes.LongLength || node.Size > int.MaxValue)
            {
                summary.Skipped++;
                Console.WriteLine($"[NODE-SKIP] index={i}, path={node.Path}, reason=InvalidRange");
                continue;
            }

            try
            {
                var nodeOut = ResolveNodeOutputPath(nodesRoot, node.Path, i, usedPaths);
                var nodeDir = Path.GetDirectoryName(nodeOut);
                if (!string.IsNullOrWhiteSpace(nodeDir))
                    Directory.CreateDirectory(nodeDir);

                var payload = new byte[node.Size];
                Buffer.BlockCopy(result.BlocksUncompressedBytes, (int)node.Offset, payload, 0, payload.Length);
                File.WriteAllBytes(nodeOut, payload);

                summary.Success++;
                Console.WriteLine($"[NODE-OK] index={i}, size={node.Size}, path={node.Path}, output={nodeOut}");
            }
            catch (Exception ex)
            {
                summary.Failed++;
                Console.Error.WriteLine($"[NODE-FAIL] index={i}, path={node.Path}, reason={ex.Message}");
            }
        }

        return summary;
    }

    private static string ResolveNodeOutputPath(string nodesRoot, string nodePath, int index, HashSet<string> usedPaths)
    {
        var safeRelative = CliHelpers.BuildSafeRelativePath(nodePath);
        if (string.IsNullOrWhiteSpace(safeRelative))
            safeRelative = $"node_{index:D4}.bin";

        var withExtension = Path.GetExtension(safeRelative);
        if (string.IsNullOrWhiteSpace(withExtension))
            safeRelative += ".bin";

        var candidate = safeRelative;
        var dedupe = 0;
        while (!usedPaths.Add(candidate))
        {
            var ext = Path.GetExtension(safeRelative);
            var baseName = string.IsNullOrWhiteSpace(ext)
                ? safeRelative
                : safeRelative[..^ext.Length];

            dedupe++;
            candidate = $"{baseName}__{index:D4}_{dedupe:D2}{ext}";
        }

        return Path.Combine(nodesRoot, candidate);
    }

    private static int FindSelectedNodeIndex(IReadOnlyList<VfsNodeInfo> nodes, VfsNodeInfo selected)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Path == selected.Path && node.Offset == selected.Offset && node.Size == selected.Size && node.Flags == selected.Flags)
                return i;
        }

        return -1;
    }

    private static string ExtractAsciiPrefix(byte[] bytes, int max)
    {
        var length = Math.Min(max, bytes.Length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            var b = bytes[i];
            chars[i] = b is >= 32 and <= 126 ? (char)b : '.';
        }

        return new string(chars);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static bool StartsWithAscii(byte[] bytes, string text)
    {
        if (bytes.Length < text.Length)
            return false;

        for (var i = 0; i < text.Length; i++)
        {
            if (bytes[i] != (byte)text[i])
                return false;
        }

        return true;
    }

    private sealed class NodeExtractSummary
    {
        public int Success { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
    }
}
