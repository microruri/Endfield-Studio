using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Endfield.BlcTool.Core.Blc;
using Endfield.BlcTool.Core.Crypto;
using Endfield.BlcTool.Core.Models;
using Endfield.Cli.App;
using Endfield.Cli.Extract;

namespace Endfield.Cli.Operations;

/// <summary>
/// Loads manifest + blc metadata from game root, finds assets by wildcard,
/// then exports required .ab bundle files while preserving virtual bundle path.
/// </summary>
public static class ExtractFilterOperation
{
    public static int Execute(string gameRoot, string filterPattern, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(filterPattern))
        {
            Console.Error.WriteLine("[FAIL] Filter pattern cannot be empty.");
            return 2;
        }

        var matcher = BuildWildcardMatcher(filterPattern);
        var patternLooksLikePath = filterPattern.Contains('/') || filterPattern.Contains('\\');

        Console.WriteLine("[STEP 1/7] Discover .blc files (Persistent first, then StreamingAssets)...");
        var selectedBlcFiles = DiscoverSelectedBlcFiles(gameRoot, out var skippedStreamingDuplicates);
        if (selectedBlcFiles.Count == 0)
        {
            Console.Error.WriteLine("[FAIL] No .blc files found from game root.");
            return 3;
        }

        Console.WriteLine($"[INFO] SelectedBlc={selectedBlcFiles.Count}, SkippedStreamingDuplicates={skippedStreamingDuplicates}");

        Console.WriteLine("[STEP 2/7] Load and parse manifest.hgmmap from BundleManifest...");
        ManifestScheme manifest;
        try
        {
            manifest = LoadManifestFromGameRoot(gameRoot);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FAIL] Failed to load manifest from game root: {ex.Message}");
            return 4;
        }

        Console.WriteLine($"[INFO] Manifest loaded: Bundles={manifest.Bundles.Count}, Assets={manifest.Assets.Count}");

        Console.WriteLine("[STEP 3/7] Filter assets by wildcard...");
        var matchedAssets = new List<MatchedAsset>();
        var invalidBundleIndex = 0;

        foreach (var asset in manifest.Assets)
        {
            var path = NormalizePath(asset.Path);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            if (!IsAssetMatch(path, matcher, patternLooksLikePath))
                continue;

            if (asset.BundleIndex < 0 || asset.BundleIndex >= manifest.Bundles.Count)
            {
                invalidBundleIndex++;
                continue;
            }

            var bundleName = NormalizePath(manifest.Bundles[asset.BundleIndex].Name);
            if (string.IsNullOrWhiteSpace(bundleName))
            {
                invalidBundleIndex++;
                continue;
            }

            matchedAssets.Add(new MatchedAsset(path, bundleName, asset.AssetSize, asset.BundleIndex));
        }

        Console.WriteLine($"[INFO] Filter={filterPattern}, MatchedAssets={matchedAssets.Count}, InvalidBundleIndexRows={invalidBundleIndex}");
        if (matchedAssets.Count == 0)
        {
            Console.WriteLine("[DONE] No matched assets.");
            return 0;
        }

        var requiredBundlePaths = matchedAssets
            .Select(x => BuildBundleVirtualPath(x.BundleName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"[INFO] RequiredBundles={requiredBundlePaths.Count}");

        Console.WriteLine("[STEP 4/7] Map required bundle files to .chk sources via .blc metadata...");
        var bundleSources = BuildBundleSourceMap(selectedBlcFiles, requiredBundlePaths);
        var unresolvedBundles = requiredBundlePaths.Count - bundleSources.Count;
        Console.WriteLine($"[INFO] ResolvedBundles={bundleSources.Count}, UnresolvedBundles={unresolvedBundles}");

        Console.WriteLine("[STEP 5/7] Extract matched bundles and keep virtual path...");
        Directory.CreateDirectory(outputPath);
        var key = KeyDeriver.GetCommonChachaKey();

        var exported = 0;
        var failed = 0;
        var missingChk = 0;

        foreach (var source in bundleSources.Values.OrderBy(x => x.File.FileName, StringComparer.OrdinalIgnoreCase))
        {
            if (TryExtractBundle(gameRoot, outputPath, source, key, out var missingReason))
            {
                exported++;
                continue;
            }

            if (missingReason)
                missingChk++;
            else
                failed++;
        }

        failed += unresolvedBundles;

        Console.WriteLine("[STEP 6/7] Write matched asset index...");
        var indexPath = Path.Combine(outputPath, "matched_assets.csv");
        WriteMatchedAssetIndex(indexPath, matchedAssets);
        Console.WriteLine($"[OK] {indexPath}");

        Console.WriteLine("[STEP 7/7] Done.");
        Console.WriteLine($"Done. MatchedAssets={matchedAssets.Count}, RequiredBundles={requiredBundlePaths.Count}, ExportedBundles={exported}, MissingChk={missingChk}, Failed={failed}");
        return failed == 0 ? 0 : 1;
    }

    private static List<string> DiscoverSelectedBlcFiles(string gameRoot, out int skippedStreamingDuplicates)
    {
        var persistentVfs = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "VFS");
        var streamingVfs = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "VFS");

        var selected = new List<string>();
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        skippedStreamingDuplicates = 0;

        if (Directory.Exists(persistentVfs))
        {
            foreach (var blc in Directory.GetFiles(persistentVfs, "*.blc", SearchOption.AllDirectories))
            {
                selected.Add(blc);
                selectedNames.Add(Path.GetFileName(blc));
            }
        }

        if (Directory.Exists(streamingVfs))
        {
            foreach (var blc in Directory.GetFiles(streamingVfs, "*.blc", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(blc);
                if (selectedNames.Contains(name))
                {
                    skippedStreamingDuplicates++;
                    continue;
                }

                selected.Add(blc);
                selectedNames.Add(name);
            }
        }

        return selected;
    }

    private static ManifestScheme LoadManifestFromGameRoot(string gameRoot)
    {
        if (!ResourceTypeRegistry.TryGetGroupHashByTypeName("BundleManifest", out var groupHash, out _))
            throw new InvalidOperationException("BundleManifest group mapping is missing.");

        var persistentBlc = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "VFS", groupHash, $"{groupHash}.blc");
        var streamingBlc = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "VFS", groupHash, $"{groupHash}.blc");
        var selectedBlc = CliHelpers.ResolvePreferredPath(persistentBlc, streamingBlc)
            ?? throw new FileNotFoundException("BundleManifest .blc not found.");

        var parsed = BlcDecoder.Decode(File.ReadAllBytes(selectedBlc));
        var manifestEntry = parsed.AllChunks
            .SelectMany(c => c.Files)
            .FirstOrDefault(f => f.FileName.EndsWith("manifest.hgmmap", StringComparison.OrdinalIgnoreCase));

        if (manifestEntry == null)
            throw new InvalidDataException("manifest.hgmmap entry not found in BundleManifest .blc.");

        var chkRel = Path.Combine("VFS", groupHash, $"{manifestEntry.FileChunkMD5Name}.chk");
        var selectedChk = CliHelpers.ResolvePreferredPath(
            Path.Combine(gameRoot, "Endfield_Data", "Persistent", chkRel),
            Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", chkRel))
            ?? throw new FileNotFoundException($"Manifest .chk not found: {chkRel}");

        if (manifestEntry.Offset < 0 || manifestEntry.Len < 0 || manifestEntry.Len > int.MaxValue)
            throw new InvalidDataException("Invalid manifest offset/len in .blc.");

        var payload = new byte[(int)manifestEntry.Len];
        using (var fs = File.Open(selectedChk, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fs.Seek(manifestEntry.Offset, SeekOrigin.Begin);
            var totalRead = 0;
            while (totalRead < payload.Length)
            {
                var read = fs.Read(payload, totalRead, payload.Length - totalRead);
                if (read <= 0)
                    break;
                totalRead += read;
            }

            if (totalRead != payload.Length)
                throw new EndOfStreamException($"Short read for manifest payload, expected={payload.Length}, actual={totalRead}");
        }

        if (manifestEntry.BUseEncrypt)
        {
            var key = KeyDeriver.GetCommonChachaKey();
            var nonce = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(nonce.AsSpan(0, 4), parsed.Version);
            BinaryPrimitives.WriteInt64LittleEndian(nonce.AsSpan(4, 8), manifestEntry.IvSeed);
            payload = ChaCha20Cipher.Decrypt(key, nonce, 1, payload);
        }

        return ManifestDecoder.Decode(payload);
    }

    private static Dictionary<string, BundleFileSource> BuildBundleSourceMap(List<string> blcPaths, HashSet<string> requiredBundlePaths)
    {
        var result = new Dictionary<string, BundleFileSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var blcPath in blcPaths)
        {
            try
            {
                var parsed = BlcDecoder.Decode(File.ReadAllBytes(blcPath));
                var groupHash = Path.GetFileNameWithoutExtension(blcPath);
                if (string.IsNullOrWhiteSpace(groupHash))
                    continue;

                foreach (var file in parsed.AllChunks.SelectMany(c => c.Files))
                {
                    var virtualPath = NormalizePath(file.FileName);
                    if (!requiredBundlePaths.Contains(virtualPath))
                        continue;

                    if (result.ContainsKey(virtualPath))
                        continue;

                    result[virtualPath] = new BundleFileSource(groupHash, parsed.Version, file);
                }
            }
            catch
            {
                // Keep extraction robust even if one .blc is malformed.
            }
        }

        return result;
    }

    private static bool TryExtractBundle(string gameRoot, string outputRoot, BundleFileSource source, byte[] key, out bool missingReason)
    {
        missingReason = false;
        var file = source.File;

        if (string.IsNullOrWhiteSpace(file.FileChunkMD5Name))
        {
            Console.Error.WriteLine($"[FAIL] Missing chunk id: {file.FileName}");
            return false;
        }

        if (file.Offset < 0 || file.Len < 0 || file.Len > int.MaxValue)
        {
            Console.Error.WriteLine($"[FAIL] Invalid range: {file.FileName}, offset={file.Offset}, len={file.Len}");
            return false;
        }

        var chkRel = Path.Combine("VFS", source.GroupHash, $"{file.FileChunkMD5Name}.chk");
        var selectedChk = CliHelpers.ResolvePreferredPath(
            Path.Combine(gameRoot, "Endfield_Data", "Persistent", chkRel),
            Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", chkRel));

        if (selectedChk == null)
        {
            Console.Error.WriteLine($"[MISS] Missing .chk: {chkRel} for {file.FileName}");
            missingReason = true;
            return false;
        }

        var payload = new byte[(int)file.Len];
        using (var fs = File.Open(selectedChk, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fs.Seek(file.Offset, SeekOrigin.Begin);
            var totalRead = 0;
            while (totalRead < payload.Length)
            {
                var read = fs.Read(payload, totalRead, payload.Length - totalRead);
                if (read <= 0)
                    break;
                totalRead += read;
            }

            if (totalRead != payload.Length)
            {
                Console.Error.WriteLine($"[FAIL] Short read: {file.FileName}, expected={payload.Length}, actual={totalRead}");
                return false;
            }
        }

        if (file.BUseEncrypt)
        {
            var nonce = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(nonce.AsSpan(0, 4), source.BlcVersion);
            BinaryPrimitives.WriteInt64LittleEndian(nonce.AsSpan(4, 8), file.IvSeed);
            payload = ChaCha20Cipher.Decrypt(key, nonce, 1, payload);
        }

        var safeRelativePath = CliHelpers.BuildSafeRelativePath(file.FileName);
        if (string.IsNullOrWhiteSpace(safeRelativePath))
        {
            Console.Error.WriteLine($"[FAIL] Invalid output path from virtual file name: {file.FileName}");
            return false;
        }

        var outFile = Path.Combine(outputRoot, safeRelativePath);
        var outDir = Path.GetDirectoryName(outFile);
        if (!string.IsNullOrWhiteSpace(outDir))
            Directory.CreateDirectory(outDir);

        File.WriteAllBytes(outFile, payload);
        return true;
    }

    private static void WriteMatchedAssetIndex(string outputCsvPath, List<MatchedAsset> rows)
    {
        using var writer = new StreamWriter(outputCsvPath, false, new UTF8Encoding(false));
        writer.WriteLine("Path,AssetSize,BundleName,BundleIndex");
        foreach (var row in rows.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteLine($"{EscapeCsv(row.Path)},{row.AssetSize},{EscapeCsv(row.BundleName)},{row.BundleIndex}");
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    private static bool IsAssetMatch(string assetPath, Regex matcher, bool patternLooksLikePath)
    {
        if (matcher.IsMatch(assetPath))
            return true;

        if (patternLooksLikePath)
            return false;

        var fileName = Path.GetFileName(assetPath);
        return matcher.IsMatch(fileName);
    }

    private static string BuildBundleVirtualPath(string bundleName)
    {
        return NormalizePath($"Data/Bundles/Windows/{bundleName}");
    }

    private static string NormalizePath(string value)
    {
        return value.Replace('\\', '/').Trim();
    }

    private static Regex BuildWildcardMatcher(string wildcard)
    {
        var normalized = wildcard.Trim().Replace('\\', '/');
        var pattern = "^" + Regex.Escape(normalized).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed record MatchedAsset(string Path, string BundleName, int AssetSize, int BundleIndex);

    private sealed record BundleFileSource(string GroupHash, int BlcVersion, BlcFileInfo File);
}
