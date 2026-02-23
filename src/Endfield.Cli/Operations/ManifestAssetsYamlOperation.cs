using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Endfield.BlcTool.Core.Blc;
using Endfield.BlcTool.Core.Crypto;
using Endfield.Cli.App;
using Endfield.Cli.Extract;

namespace Endfield.Cli.Operations;

/// <summary>
/// Reads BundleManifest from VFS, parses manifest assets in memory,
/// and writes a tree-style YAML index for asset paths.
///
/// Note:
/// Manifest assets can contain exact duplicate rows in real game data
/// (same path and same metadata repeated many times). This operation
/// deduplicates by normalized full asset path before writing YAML.
/// </summary>
public static class ManifestAssetsYamlOperation
{
    public static int Execute(string gameRoot, string outputPath)
    {
        Console.WriteLine("[STEP 1/6] Resolve BundleManifest group...");
        if (!ResourceTypeRegistry.TryGetGroupHashByTypeName("BundleManifest", out var groupHash, out _))
        {
            Console.Error.WriteLine("[FAIL] BundleManifest type mapping is missing.");
            return 2;
        }

        var persistentVfsRoot = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "VFS");
        var streamingVfsRoot = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "VFS");
        var persistentDataRoot = Path.Combine(gameRoot, "Endfield_Data", "Persistent");
        var streamingDataRoot = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets");

        var persistentBlcPath = Path.Combine(persistentVfsRoot, groupHash, $"{groupHash}.blc");
        var streamingBlcPath = Path.Combine(streamingVfsRoot, groupHash, $"{groupHash}.blc");

        Console.WriteLine("[STEP 2/6] Locate and parse BundleManifest .blc...");
        var selectedBlc = CliHelpers.ResolvePreferredPath(persistentBlcPath, streamingBlcPath);
        if (selectedBlc == null)
        {
            Console.Error.WriteLine("[FAIL] BundleManifest .blc not found.");
            Console.Error.WriteLine($"  - {persistentBlcPath}");
            Console.Error.WriteLine($"  - {streamingBlcPath}");
            return 3;
        }

        var blc = BlcDecoder.Decode(File.ReadAllBytes(selectedBlc));
        var manifestFile = blc.AllChunks
            .SelectMany(c => c.Files)
            .FirstOrDefault(f => f.FileName.EndsWith("manifest.hgmmap", StringComparison.OrdinalIgnoreCase));

        if (manifestFile == null)
        {
            Console.Error.WriteLine("[FAIL] manifest.hgmmap entry not found in BundleManifest .blc.");
            return 4;
        }

        Console.WriteLine($"[INFO] Found manifest entry: {manifestFile.FileName}, chunk={manifestFile.FileChunkMD5Name}, len={manifestFile.Len}");

        Console.WriteLine("[STEP 3/6] Locate source .chk and read payload...");
        var relChk = Path.Combine("VFS", groupHash, $"{manifestFile.FileChunkMD5Name}.chk");
        var selectedChk = CliHelpers.ResolvePreferredPath(
            Path.Combine(gameRoot, "Endfield_Data", "Persistent", relChk),
            Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", relChk));

        if (selectedChk == null)
        {
            Console.Error.WriteLine($"[FAIL] Required .chk not found: {relChk}");
            return 4;
        }

        var chkSource = selectedChk.StartsWith(persistentDataRoot, StringComparison.OrdinalIgnoreCase)
            ? "Persistent"
            : "StreamingAssets";
        Console.WriteLine($"[INFO] Selected .chk: {selectedChk} ({chkSource})");

        if (manifestFile.Offset < 0 || manifestFile.Len < 0 || manifestFile.Len > int.MaxValue)
        {
            Console.Error.WriteLine("[FAIL] Invalid manifest range in .blc metadata.");
            return 4;
        }

        var payload = new byte[(int)manifestFile.Len];
        using (var chkStream = File.Open(selectedChk, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            chkStream.Seek(manifestFile.Offset, SeekOrigin.Begin);
            var totalRead = 0;
            while (totalRead < payload.Length)
            {
                var read = chkStream.Read(payload, totalRead, payload.Length - totalRead);
                if (read <= 0)
                    break;

                totalRead += read;
            }

            if (totalRead != payload.Length)
            {
                Console.Error.WriteLine($"[FAIL] Short read for manifest payload, expected={payload.Length}, actual={totalRead}");
                return 4;
            }
        }

        if (manifestFile.BUseEncrypt)
        {
            var key = KeyDeriver.GetCommonChachaKey();
            var nonce = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(nonce.AsSpan(0, 4), blc.Version);
            BinaryPrimitives.WriteInt64LittleEndian(nonce.AsSpan(4, 8), manifestFile.IvSeed);
            payload = ChaCha20Cipher.Decrypt(key, nonce, 1, payload);
        }

        Console.WriteLine("[STEP 4/6] Parse manifest (Assets in memory)...");
        var manifest = ManifestDecoder.Decode(payload);
        Console.WriteLine($"[INFO] Manifest parsed: Bundles={manifest.Bundles.Count}, Assets={manifest.Assets.Count}, DataAddress={manifest.DataAddress}, BundleArrayAddress={manifest.BundleArrayAddress}");

        Console.WriteLine("[STEP 5/6] Build asset path tree and write YAML...");
        var root = BuildAssetTree(manifest.Assets, out var duplicateRowsRemoved);
        var outputFile = ResolveYamlOutputPath(outputPath);
        var outputDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        using (var writer = new StreamWriter(outputFile, false, new UTF8Encoding(false)))
            WriteYaml(root, writer);

        Console.WriteLine("[STEP 6/6] Finished.");
        Console.WriteLine($"Done. YAML={outputFile}, Assets={manifest.Assets.Count}, DedupedRows={duplicateRowsRemoved}");
        return 0;
    }

    private static AssetTreeNode BuildAssetTree(IEnumerable<ManifestAssetInfo> assets, out int duplicateRowsRemoved)
    {
        // Exact duplicates are known to exist in manifest assets. Keep a set of
        // normalized full paths so YAML output is concise and deterministic.
        var root = new AssetTreeNode();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        duplicateRowsRemoved = 0;

        foreach (var asset in assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Path))
                continue;

            var normalizedPath = asset.Path.Replace('\\', '/').Trim();
            if (!seenPaths.Add(normalizedPath))
            {
                duplicateRowsRemoved++;
                continue;
            }

            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // Most paths start with "assets/...". Since root key is already "assets",
            // remove that leading segment to avoid "assets -> assets -> ...".
            if (parts.Length > 1 && parts[0].Equals("assets", StringComparison.OrdinalIgnoreCase))
                parts = parts[1..];

            if (parts.Length == 0)
                continue;

            var node = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i].Trim();
                if (part.Length == 0)
                    continue;

                if (!node.Children.TryGetValue(part, out var child))
                {
                    child = new AssetTreeNode();
                    node.Children[part] = child;
                }

                node = child;
            }

            var fileName = parts[^1].Trim();
            if (fileName.Length == 0)
                continue;
            node.Files.Add(fileName);
        }

        return root;
    }

    private static void WriteYaml(AssetTreeNode root, TextWriter writer)
    {
        writer.WriteLine("assets:");
        WriteNodeChildren(root, writer, 2);
    }

    private static void WriteNodeChildren(AssetTreeNode node, TextWriter writer, int indent)
    {
        foreach (var child in node.Children.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ind = new string(' ', indent);
            writer.WriteLine($"{ind}{FormatYamlKey(child.Key)}:");
            WriteNodeContent(child.Value, writer, indent + 2);
        }
    }

    private static void WriteNodeContent(AssetTreeNode node, TextWriter writer, int indent)
    {
        WriteNodeChildren(node, writer, indent);

        if (node.Files.Count == 0)
            return;

        var entriesIndent = new string(' ', indent);
        if (node.Children.Count > 0)
        {
            writer.WriteLine($"{entriesIndent}_files:");
            entriesIndent = new string(' ', indent + 2);
        }

        foreach (var file in node.Files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteLine($"{entriesIndent}- {FormatYamlFileName(file)}");
        }
    }

    private static string ResolveYamlOutputPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return Path.Combine(Directory.GetCurrentDirectory(), "bundle_manifest_assets.yaml");

        if (outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith(Path.AltDirectorySeparatorChar) || string.IsNullOrEmpty(Path.GetExtension(outputPath)))
            return Path.Combine(outputPath, "bundle_manifest_assets.yaml");

        return outputPath;
    }

    private static string FormatYamlKey(string key)
    {
        if (IsPlainYamlToken(key))
            return key;

        return QuoteYamlString(key);
    }

    private static bool IsPlainYamlToken(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                continue;

            if (ch is '-' or '_' or '/' or '.' or '+')
                continue;

            return false;
        }

        return true;
    }

    private static string QuoteYamlString(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }

    private static string FormatYamlFileName(string fileName)
    {
        return IsPlainYamlToken(fileName) ? fileName : QuoteYamlString(fileName);
    }

    private sealed class AssetTreeNode
    {
        public Dictionary<string, AssetTreeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Files { get; } = new();
    }
}
