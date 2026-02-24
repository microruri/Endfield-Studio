using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Endfield.BlcTool.Core.Blc;
using Endfield.BlcTool.Core.Crypto;
using Endfield.BlcTool.Core.Models;
using Endfield.Tool.CLI.App;
using Endfield.Tool.CLI.Decode;
using Endfield.Tool.CLI.Decode.InitAudio;
using Endfield.Tool.CLI.Models;
using Endfield.JsonTool.Core.Json;

namespace Endfield.Tool.CLI.Operations;

/// <summary>
/// Extracts files for one initial resource type by loading index_initial.json and matching .blc metadata.
/// </summary>
public static class ExtractOperation
{
    private const string PersistentSource = "Persistent";
    private const string StreamingSource = "StreamingAssets";
    private const string IndexInitial = "index_initial.json";
    private const string IndexMain = "index_main.json";

    /// <summary>
    /// Executes extract flow for one supported initial resource type.
    /// </summary>
    public static int Execute(string gameRoot, string resourceTypeName, string outputPath, bool decodeContent)
    {
        Console.WriteLine("[STEP 1/6] Resolve requested resource type...");
        if (!ResourceTypeRegistry.TryGetByName(resourceTypeName, out var typeInfo))
        {
            Console.Error.WriteLine($"Unknown resource type name: {resourceTypeName}");
            Console.Error.WriteLine("Supported names:");
            foreach (var name in ResourceTypeRegistry.GetSupportedTypeNames())
                Console.Error.WriteLine($"  - {name}");

            return 2;
        }

        Console.WriteLine($"[INFO] Type: {typeInfo.Name}, TypeId={typeInfo.TypeId}");
        if (decodeContent)
        {
            if (!DecodeContentProcessor.IsDecodeSupportedForType(typeInfo.Name))
            {
                Console.Error.WriteLine($"[FAIL] Decode/decrypt (-d) is not supported for resource type: {typeInfo.Name}");
                return 2;
            }

            Console.WriteLine("[INFO] Decode/decrypt mode: enabled (-d)");
        }

        Console.WriteLine("[STEP 2/6] Load and parse available index files (Persistent first)...");
        var indexLoads = LoadAvailableIndices(gameRoot);
        if (indexLoads.Count == 0)
        {
            Console.Error.WriteLine("[FAIL] Neither index_initial.json nor index_main.json was found in Persistent/StreamingAssets.");
            return 3;
        }

        foreach (var indexLoad in indexLoads)
            Console.WriteLine($"[INFO] Loaded index: {indexLoad.IndexFileName} ({indexLoad.Source}), entries={indexLoad.Model.Files.Count}");

        Console.WriteLine("[STEP 3/6] Filter target .blc from loaded index files by resource type...");
        var targetBlc = FindTargetBlcEntry(indexLoads, typeInfo.TypeId);
        if (targetBlc == null)
        {
            Console.Error.WriteLine("[FAIL] No .blc entry found in loaded indexes for the requested type.");
            return 3;
        }

        var blcRelativePath = targetBlc.Entry.Name;
        var blcPath = ResolveFromGameDataRoots(gameRoot, blcRelativePath);
        if (blcPath == null)
        {
            Console.Error.WriteLine("[FAIL] Target .blc file was not found in Persistent or StreamingAssets.");
            Console.Error.WriteLine($"  - {ToGameRelativeDisplayPath(PersistentSource, blcRelativePath)}");
            Console.Error.WriteLine($"  - {ToGameRelativeDisplayPath(StreamingSource, blcRelativePath)}");
            return 3;
        }

        Console.WriteLine($"[INFO] Target .blc from index: {targetBlc.Entry.Name}");
        Console.WriteLine($"[INFO] Selected index for type: {targetBlc.Index.IndexFileName} ({targetBlc.Index.Source})");
        Console.WriteLine($"[INFO] Selected .blc file: {ToGameRelativeDisplayPath(blcPath.Source, blcRelativePath)}");

        Console.WriteLine("[STEP 4/6] Decode .blc and build extraction plan...");
        var parsed = BlcDecoder.Decode(File.ReadAllBytes(blcPath.Path));
        var allFiles = parsed.AllChunks.SelectMany(c => c.Files).ToList();
        Console.WriteLine($"[INFO] GroupCfgName={parsed.GroupCfgName}, Chunks={parsed.AllChunks.Count}, Files={allFiles.Count}");

        var plan = BuildChunkLoadPlan(gameRoot, blcRelativePath, allFiles);
        Console.WriteLine($"[INFO] Chk plan: required={plan.RequiredCount}, loadable={plan.LoadPlans.Count}, missing={plan.MissingChunkIds.Count}");
        foreach (var missingChunkId in plan.MissingChunkIds)
            Console.WriteLine($"[CHK-MISS] {missingChunkId}");

        Console.WriteLine("[STEP 5/6] Extract files in .blc order (single-thread)...");
        var stats = ExtractFilesSequential(outputPath, parsed.Version, typeInfo.Name, decodeContent, allFiles, plan.LoadPlans);

        Console.WriteLine("[STEP 6/6] Finished extraction.");
        Console.WriteLine($"Done. Success={stats.Success}, Failed={stats.Failed}, MissingChunks={plan.MissingChunkIds.Count}");
        return stats.Failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Loads all known index files from game root.
    /// </summary>
    private static List<IndexLoadResult> LoadAvailableIndices(string gameRoot)
    {
        var results = new List<IndexLoadResult>(2);

        var initial = LoadIndexFromGameRoot(gameRoot, IndexInitial);
        if (initial != null)
            results.Add(initial);

        var main = LoadIndexFromGameRoot(gameRoot, IndexMain);
        if (main != null)
            results.Add(main);

        return results;
    }

    /// <summary>
    /// Loads and decrypts one index file from game root.
    /// </summary>
    private static IndexLoadResult? LoadIndexFromGameRoot(string gameRoot, string indexFileName)
    {
        var resolved = ResolveFromGameDataRoots(gameRoot, indexFileName);
        if (resolved == null)
            return null;

        var model = LoadIndexInitial(resolved.Path);
        return new IndexLoadResult(model, resolved.Source, indexFileName);
    }

    /// <summary>
    /// Locates the .blc entry for the requested type id across loaded indexes.
    /// </summary>
    private static BlcSelectionResult? FindTargetBlcEntry(List<IndexLoadResult> indexLoads, int typeId)
    {
        var ordered = indexLoads
            .OrderBy(x => GetIndexPriority(x.IndexFileName))
            .ThenBy(x => x.IndexFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var indexLoad in ordered)
        {
            var entry = indexLoad.Model.Files
                .Where(f => f.Type == typeId)
                .FirstOrDefault(f => f.Name.EndsWith(".blc", StringComparison.OrdinalIgnoreCase));

            if (entry != null)
                return new BlcSelectionResult(indexLoad, entry);
        }

        return null;
    }

    private static int GetIndexPriority(string indexFileName)
    {
        return indexFileName.Equals(IndexInitial, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    /// <summary>
    /// Resolves one relative path from Persistent first, then StreamingAssets.
    /// </summary>
    private static PathResolutionResult? ResolveFromGameDataRoots(string gameRoot, string relativePath)
    {
        var persistentPath = Path.Combine(gameRoot, "Endfield_Data", PersistentSource, relativePath);
        var streamingPath = Path.Combine(gameRoot, "Endfield_Data", StreamingSource, relativePath);
        var selectedPath = CliHelpers.ResolvePreferredPath(persistentPath, streamingPath);
        if (selectedPath == null)
            return null;

        var source = selectedPath.Equals(persistentPath, StringComparison.OrdinalIgnoreCase)
            ? PersistentSource
            : StreamingSource;
        return new PathResolutionResult(selectedPath, source);
    }

    /// <summary>
    /// Builds chunk-to-file mapping and resolves required .chk paths.
    /// </summary>
    private static ChkPlanResult BuildChunkLoadPlan(string gameRoot, string blcRelativePath, List<BlcFileInfo> allFiles)
    {
        var chunkToFiles = allFiles
            .GroupBy(f => f.FileChunkMD5Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var blcRelativeDirectory = Path.GetDirectoryName(blcRelativePath) ?? string.Empty;
        var loadPlans = new List<ChkLoadPlan>();
        var missingChunkIds = new List<string>();

        foreach (var pair in chunkToFiles.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var chunkId = pair.Key;
            if (string.IsNullOrWhiteSpace(chunkId))
                continue;

            var relChk = Path.Combine(blcRelativeDirectory, $"{chunkId}.chk");
            var chkPath = ResolveFromGameDataRoots(gameRoot, relChk);
            if (chkPath == null)
            {
                missingChunkIds.Add(chunkId);
                continue;
            }

            loadPlans.Add(new ChkLoadPlan(chunkId, chkPath.Path));
        }

        return new ChkPlanResult(chunkToFiles.Count, loadPlans, missingChunkIds);
    }

    /// <summary>
    /// Extracts all files sequentially in the same order as .blc metadata.
    /// </summary>
    private static ExtractionStats ExtractFilesSequential(string outputPath, int blcVersion, string resourceTypeName, bool decodeContent, List<BlcFileInfo> allFiles, List<ChkLoadPlan> loadPlans)
    {
        Directory.CreateDirectory(outputPath);
        var key = KeyDeriver.GetCommonChachaKey();
        var chunkFileCache = BuildChunkFileCache(loadPlans);

        var success = 0;
        var failed = 0;

        // TODO: parallelize extraction by chunk or file with bounded concurrency.
        foreach (var file in allFiles)
        {
            if (TryExtractOneFile(file, blcVersion, resourceTypeName, decodeContent, key, chunkFileCache, outputPath, out var errorMessage))
            {
                success++;
                continue;
            }

            failed++;
            Console.Error.WriteLine(errorMessage);
        }

        return new ExtractionStats(success, failed);
    }

    /// <summary>
    /// Loads all required .chk files into memory for fast sequential reads.
    /// </summary>
    private static Dictionary<string, byte[]> BuildChunkFileCache(List<ChkLoadPlan> loadPlans)
    {
        var cache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in loadPlans)
            cache[plan.ChunkId] = File.ReadAllBytes(plan.ChkPath);

        return cache;
    }

    /// <summary>
    /// Extracts one virtual file from chunk cache and writes it to output path.
    /// </summary>
    private static bool TryExtractOneFile(BlcFileInfo file, int blcVersion, string resourceTypeName, bool decodeContent, byte[] key, Dictionary<string, byte[]> chunkFileCache, string outputPath, out string errorMessage)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(file.FileChunkMD5Name) || !chunkFileCache.TryGetValue(file.FileChunkMD5Name, out var chkBytes))
            {
                errorMessage = $"[MISS] Missing .chk data for file={file.FileName}, chunk={file.FileChunkMD5Name}";
                return false;
            }

            if (!TryReadPayload(file, chkBytes, out var payload, out errorMessage))
                return false;

            if (file.BUseEncrypt)
                payload = DecryptPayload(payload, blcVersion, file.IvSeed, key);

            if (decodeContent)
            {
                if (IsAudioPckUnpackType(resourceTypeName) && file.FileName.EndsWith(".pck", StringComparison.OrdinalIgnoreCase))
                {
                    if (!PckDecryptor.TryDecrypt(payload, out payload, out var decryptMessage))
                    {
                        errorMessage = $"[FAIL-DECODE] {file.FileName}: {decryptMessage}";
                        return false;
                    }

                    Console.WriteLine($"[INFO] {decryptMessage}");
                }

                if (!DecodeContentProcessor.TryProcess(resourceTypeName, file.FileName, payload, out payload, out var decodeMessage))
                {
                    errorMessage = $"[FAIL-DECODE] {file.FileName}: {decodeMessage}";
                    return false;
                }
            }

            var safeRelative = CliHelpers.BuildSafeRelativePath(file.FileName);
            if (string.IsNullOrWhiteSpace(safeRelative))
            {
                errorMessage = $"[FAIL] Invalid output path from virtual name: {file.FileName}";
                return false;
            }

            var outFile = Path.Combine(outputPath, safeRelative);
            var outDir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);

            File.WriteAllBytes(outFile, payload);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"[FAIL] {file.FileName}: {ex.Message}";
            return false;
        }
    }

    private static bool IsAudioPckUnpackType(string resourceTypeName)
    {
        return resourceTypeName is "InitAudio" or "Audio" or "AudioChinese" or "AudioEnglish" or "AudioJapanese" or "AudioKorean";
    }

    /// <summary>
    /// Reads one file slice from a chunk buffer and validates boundaries.
    /// </summary>
    private static bool TryReadPayload(BlcFileInfo file, byte[] chkBytes, out byte[] payload, out string errorMessage)
    {
        payload = Array.Empty<byte>();
        if (file.Offset < 0 || file.Len < 0 || file.Len > int.MaxValue)
        {
            errorMessage = $"[FAIL] Invalid offset/len for file={file.FileName}, offset={file.Offset}, len={file.Len}";
            return false;
        }

        var start = (int)file.Offset;
        var length = (int)file.Len;
        if (start > chkBytes.Length || start + length > chkBytes.Length)
        {
            errorMessage = $"[FAIL] Out-of-range read for file={file.FileName}, offset={file.Offset}, len={file.Len}, chkSize={chkBytes.Length}";
            return false;
        }

        payload = new byte[length];
        Buffer.BlockCopy(chkBytes, start, payload, 0, length);
        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Decrypts one file payload with per-file nonce values from .blc metadata.
    /// </summary>
    private static byte[] DecryptPayload(byte[] payload, int blcVersion, long ivSeed, byte[] key)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(nonce.AsSpan(0, 4), blcVersion);
        BinaryPrimitives.WriteInt64LittleEndian(nonce.AsSpan(4, 8), ivSeed);
        return ChaCha20Cipher.Decrypt(key, nonce, 1, payload);
    }

    /// <summary>
    /// Formats a game-root-relative display path for logs.
    /// </summary>
    private static string ToGameRelativeDisplayPath(string source, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return $"Endfield_Data/{source}/{normalized}";
    }

    /// <summary>
    /// Decrypts index file and parses JSON payload.
    /// </summary>
    private static IndexInitialModel LoadIndexInitial(string indexPath)
    {
        var encryptedBytes = File.ReadAllBytes(indexPath);
        var firstStageBytes = JsonDecryptor.DecryptFirstStage(encryptedBytes);
        if (!JsonDecryptor.TryDecodeUtf8Json(firstStageBytes, out var plainJson))
            throw new InvalidDataException("index_initial.json decrypted bytes are not UTF-8 JSON text.");

        var parsed = JsonSerializer.Deserialize<IndexInitialModel>(plainJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (parsed == null)
            throw new InvalidDataException("Failed to parse index_initial.json.");

        return parsed;
    }

    private sealed record PathResolutionResult(string Path, string Source);
    private sealed record IndexLoadResult(IndexInitialModel Model, string Source, string IndexFileName);
    private sealed record BlcSelectionResult(IndexLoadResult Index, IndexFileEntry Entry);
    private sealed record ChkLoadPlan(string ChunkId, string ChkPath);
    private sealed record ChkPlanResult(int RequiredCount, List<ChkLoadPlan> LoadPlans, List<string> MissingChunkIds);
    private sealed record ExtractionStats(int Success, int Failed);
}
