using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Endfield.BlcTool.Core.Blc;
using Endfield.BlcTool.Core.Crypto;
using Endfield.BlcTool.Core.Models;
using Endfield.Cli.App;
using Endfield.Cli.Extract;

namespace Endfield.Cli.Operations;

/// <summary>
/// Extracts all virtual files for one resource type from .chk containers.
/// </summary>
public static class ExtractTypeOperation
{
    public static int Execute(string gameRoot, string resourceTypeName, string outputPath, bool decodeContent)
    {
        Console.WriteLine("[STEP 1/6] Resolve requested resource type...");
        if (!ResourceTypeRegistry.TryGetGroupHashByTypeName(resourceTypeName, out var groupHash, out var canonicalName))
        {
            Console.Error.WriteLine($"Unknown resource type name: {resourceTypeName}");
            Console.Error.WriteLine("Supported names:");
            foreach (var name in ResourceTypeRegistry.GetSupportedTypeNames())
                Console.Error.WriteLine($"  - {name}");

            return 2;
        }

        Console.WriteLine($"[INFO] Type: {canonicalName}, GroupHash: {groupHash}");

        var persistentVfsRoot = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "VFS");
        var streamingVfsRoot = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "VFS");
        var persistentDataRoot = Path.Combine(gameRoot, "Endfield_Data", "Persistent");
        var streamingDataRoot = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets");

        var persistentBlcPath = Path.Combine(persistentVfsRoot, groupHash, $"{groupHash}.blc");
        var streamingBlcPath = Path.Combine(streamingVfsRoot, groupHash, $"{groupHash}.blc");

        Console.WriteLine("[STEP 2/6] Locate source .blc (Persistent first, then StreamingAssets)...");
        var selectedBlc = CliHelpers.ResolvePreferredPath(persistentBlcPath, streamingBlcPath);
        if (selectedBlc == null)
        {
            Console.Error.WriteLine("[FAIL] Target .blc file was not found in either source root.");
            Console.Error.WriteLine($"  - {persistentBlcPath}");
            Console.Error.WriteLine($"  - {streamingBlcPath}");
            return 3;
        }

        var blcSource = selectedBlc.StartsWith(persistentVfsRoot, StringComparison.OrdinalIgnoreCase)
            ? "Persistent"
            : "StreamingAssets";
        Console.WriteLine($"[INFO] Selected .blc: {selectedBlc} ({blcSource})");

        Console.WriteLine("[STEP 3/6] Decode .blc metadata...");
        var parsed = BlcDecoder.Decode(File.ReadAllBytes(selectedBlc));
        var allFiles = parsed.AllChunks.SelectMany(c => c.Files).ToList();
        Console.WriteLine($"[INFO] GroupCfgName={parsed.GroupCfgName}, TotalFiles={allFiles.Count}, Chunks={parsed.AllChunks.Count}");

        Console.WriteLine("[STEP 4/6] Build .chk load plan and print summary...");
        var chunkToFiles = allFiles
            .GroupBy(f => f.FileChunkMD5Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var plans = new List<ChkLoadPlan>();
        var missingChunkIds = new List<string>();
        var invalidChunkIdFileCount = 0;

        foreach (var chunkId in chunkToFiles.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var filesInChunk = chunkToFiles[chunkId];
            if (string.IsNullOrWhiteSpace(chunkId))
            {
                invalidChunkIdFileCount += filesInChunk.Count;
                continue;
            }

            var relChk = Path.Combine("VFS", groupHash, $"{chunkId}.chk");
            var persistentChk = Path.Combine(gameRoot, "Endfield_Data", "Persistent", relChk);
            var streamingChk = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", relChk);
            var selectedChk = CliHelpers.ResolvePreferredPath(persistentChk, streamingChk);

            if (selectedChk == null)
            {
                missingChunkIds.Add(chunkId);
                continue;
            }

            var source = selectedChk.StartsWith(persistentDataRoot, StringComparison.OrdinalIgnoreCase)
                ? "Persistent"
                : "StreamingAssets";

            plans.Add(new ChkLoadPlan(
                chunkId,
                selectedChk,
                source,
                new FileInfo(selectedChk).Length,
                filesInChunk));
        }

        long totalChkBytes = 0;
        var fromPersistent = 0;
        var fromStreaming = 0;
        foreach (var plan in plans)
        {
            totalChkBytes += plan.SizeBytes;
            if (plan.Source == "Persistent")
                fromPersistent++;
            else
                fromStreaming++;

            Console.WriteLine($"[CHK-PLAN] {plan.ChunkId} | source={plan.Source} | size={FormatBytes(plan.SizeBytes)} ({plan.SizeBytes} B) | files={plan.Files.Count}");
        }

        foreach (var chunkId in missingChunkIds)
            Console.WriteLine($"[CHK-MISS] {chunkId}");

        Console.WriteLine($"[INFO] ChkSummary: RequiredChunks={chunkToFiles.Count}, LoadableChunks={plans.Count}, MissingChunks={missingChunkIds.Count}, TotalLoadSize={FormatBytes(totalChkBytes)} ({totalChkBytes} B)");
        Console.WriteLine($"[INFO] ChkSource: Persistent={fromPersistent}, StreamingAssets={fromStreaming}");
        if (invalidChunkIdFileCount > 0)
            Console.WriteLine($"[WARN] Files with empty chunk id: {invalidChunkIdFileCount}");

        Console.WriteLine("[STEP 5/6] Extract resource files from .chk containers...");
        Directory.CreateDirectory(outputPath);
        var key = KeyDeriver.GetCommonChachaKey();

        var success = 0;
        var failed = 0;
        var missingChk = missingChunkIds.Count;

        var totalFiles = allFiles.Count;
        var processed = 0;
        var reportEvery = Math.Max(1, totalFiles / 50); // around 50 count-based updates
        const int TimeReportIntervalMs = 3000; // plus periodic heartbeat every ~3s
        var nextTimeReportTick = Environment.TickCount64 + TimeReportIntervalMs;
        Console.WriteLine($"[PROGRESS] 0/{totalFiles} (0.0%)");

        foreach (var plan in plans)
        {
            using var chkStream = File.Open(plan.ChkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            foreach (var file in plan.Files)
            {
                try
                {
                    if (file.Offset < 0 || file.Len < 0)
                    {
                        failed++;
                        Console.Error.WriteLine($"[FAIL] Invalid offset/len for file={file.FileName}, offset={file.Offset}, len={file.Len}");
                        goto ProgressUpdate;
                    }

                    if (file.Len > int.MaxValue)
                    {
                        failed++;
                        Console.Error.WriteLine($"[FAIL] File too large for current extractor (>2GB): {file.FileName}");
                        goto ProgressUpdate;
                    }

                    var length = (int)file.Len;
                    var payload = new byte[length];

                    chkStream.Seek(file.Offset, SeekOrigin.Begin);
                    var totalRead = 0;
                    while (totalRead < length)
                    {
                        var read = chkStream.Read(payload, totalRead, length - totalRead);
                        if (read <= 0)
                            break;

                        totalRead += read;
                    }

                    if (totalRead != length)
                    {
                        failed++;
                        Console.Error.WriteLine($"[FAIL] Short read for file={file.FileName}, expected={length}, actual={totalRead}");
                        goto ProgressUpdate;
                    }

                    if (file.BUseEncrypt)
                    {
                        var nonce = new byte[12];
                        BinaryPrimitives.WriteInt32LittleEndian(nonce.AsSpan(0, 4), parsed.Version);
                        BinaryPrimitives.WriteInt64LittleEndian(nonce.AsSpan(4, 8), file.IvSeed);
                        payload = ChaCha20Cipher.Decrypt(key, nonce, 1, payload);
                    }

                    payload = TryDecodeExtractedPayload(payload, file.FileName, decodeContent);

                    var safeRelative = CliHelpers.BuildSafeRelativePath(file.FileName);
                    if (string.IsNullOrWhiteSpace(safeRelative))
                    {
                        failed++;
                        Console.Error.WriteLine($"[FAIL] Invalid output file path from virtual name: {file.FileName}");
                        goto ProgressUpdate;
                    }

                    var outFile = Path.Combine(outputPath, safeRelative);
                    var outDir = Path.GetDirectoryName(outFile);
                    if (!string.IsNullOrEmpty(outDir))
                        Directory.CreateDirectory(outDir);

                    File.WriteAllBytes(outFile, payload);
                    success++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"[FAIL] {file.FileName}: {ex.Message}");
                }

            ProgressUpdate:
                processed++;
                ReportProgressIfNeeded(processed, totalFiles, reportEvery, ref nextTimeReportTick, success, failed);
            }
        }

        if (missingChunkIds.Count > 0)
        {
            foreach (var missingChunkId in missingChunkIds)
            {
                if (!chunkToFiles.TryGetValue(missingChunkId, out var filesInMissingChunk))
                    continue;

                foreach (var file in filesInMissingChunk)
                {
                    failed++;
                    processed++;
                    Console.Error.WriteLine($"[MISS] Missing .chk for chunk={missingChunkId}, file={file.FileName}");
                    ReportProgressIfNeeded(processed, totalFiles, reportEvery, ref nextTimeReportTick, success, failed);
                }
            }
        }

        if (invalidChunkIdFileCount > 0)
        {
            failed += invalidChunkIdFileCount;
            for (var i = 0; i < invalidChunkIdFileCount; i++)
            {
                processed++;
                ReportProgressIfNeeded(processed, totalFiles, reportEvery, ref nextTimeReportTick, success, failed);
            }
        }

        Console.WriteLine("[STEP 6/6] Finished extraction.");
        Console.WriteLine($"Done. Success={success}, Failed={failed}, MissingChk={missingChk}, ChkFromPersistent={fromPersistent}, ChkFromStreaming={fromStreaming}");
        return failed == 0 ? 0 : 1;
    }

    private static byte[] TryDecodeExtractedPayload(byte[] payload, string fileName, bool decodeContent)
    {
        if (!decodeContent)
            return payload;

        if (!fileName.EndsWith(".hgmmap", StringComparison.OrdinalIgnoreCase))
            return payload;

        try
        {
            var manifest = ManifestDecoder.Decode(payload);
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifest, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] Failed to decode manifest content for {fileName}: {ex.Message}");
            return payload;
        }
    }

    private static void ReportProgressIfNeeded(int processed, int totalFiles, int reportEvery, ref long nextTimeReportTick, int success, int failed)
    {
        var nowTick = Environment.TickCount64;
        var shouldReportByCount = processed == totalFiles || processed % reportEvery == 0;
        var shouldReportByTime = nowTick >= nextTimeReportTick;
        if (!shouldReportByCount && !shouldReportByTime)
            return;

        var percent = totalFiles == 0 ? 100.0 : (double)processed / totalFiles * 100.0;
        Console.WriteLine($"[PROGRESS] {processed}/{totalFiles} ({percent:F1}%), success={success}, failed={failed}");

        if (shouldReportByTime)
            nextTimeReportTick = nowTick + 3000;
    }

    private static string FormatBytes(long bytes)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;

        if (bytes >= gb)
            return $"{bytes / (double)gb:F2} GiB";

        if (bytes >= mb)
            return $"{bytes / (double)mb:F2} MiB";

        if (bytes >= kb)
            return $"{bytes / (double)kb:F2} KiB";

        return $"{bytes} B";
    }

    private sealed record ChkLoadPlan(
        string ChunkId,
        string ChkPath,
        string Source,
        long SizeBytes,
        List<BlcFileInfo> Files);
}
