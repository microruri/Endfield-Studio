using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Endfield.BlcTool.Core.Blc;
using Endfield.BlcTool.Core.Crypto;
using Endfield.Cli.App;

namespace Endfield.Cli.Operations;

/// <summary>
/// Extracts all virtual files for one resource type from .chk containers.
/// </summary>
public static class ExtractTypeOperation
{
    public static int Execute(string gameRoot, string resourceTypeName, string outputPath)
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

        Console.WriteLine("[STEP 4/6] Prepare output directory and crypto context...");
        Directory.CreateDirectory(outputPath);
        var key = KeyDeriver.GetCommonChachaKey();
        Console.WriteLine($"[INFO] CHK roots: Persistent={persistentDataRoot}, StreamingAssets={streamingDataRoot}");

        Console.WriteLine("[STEP 5/6] Extract resource files from .chk containers...");

        var success = 0;
        var failed = 0;
        var missingChk = 0;
        var fromPersistent = 0;
        var fromStreaming = 0;

        var chkPathCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var chkStreamCache = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);

        try
        {
            for (var i = 0; i < allFiles.Count; i++)
            {
                var file = allFiles[i];
                if (i % 5000 == 0)
                    Console.WriteLine($"[PROGRESS] Processing {i}/{allFiles.Count}...");

                try
                {
                    var chunkId = file.FileChunkMD5Name;
                    if (string.IsNullOrWhiteSpace(chunkId))
                    {
                        failed++;
                        Console.Error.WriteLine($"[FAIL] Empty chunk id for file: {file.FileName}");
                        continue;
                    }

                    if (!chkPathCache.TryGetValue(chunkId, out var selectedChk))
                    {
                        var relChk = Path.Combine("VFS", groupHash, $"{chunkId}.chk");
                        var persistentChk = Path.Combine(gameRoot, "Endfield_Data", "Persistent", relChk);
                        var streamingChk = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", relChk);
                        selectedChk = CliHelpers.ResolvePreferredPath(persistentChk, streamingChk);
                        chkPathCache[chunkId] = selectedChk;

                        if (selectedChk != null)
                        {
                            if (selectedChk.StartsWith(persistentDataRoot, StringComparison.OrdinalIgnoreCase))
                                fromPersistent++;
                            else
                                fromStreaming++;
                        }
                    }

                    if (selectedChk == null)
                    {
                        missingChk++;
                        failed++;
                        Console.Error.WriteLine($"[MISS] Missing .chk for chunk={chunkId}, file={file.FileName}");
                        continue;
                    }

                    if (!chkStreamCache.TryGetValue(selectedChk, out var chkStream))
                    {
                        chkStream = File.Open(selectedChk, FileMode.Open, FileAccess.Read, FileShare.Read);
                        chkStreamCache[selectedChk] = chkStream;
                    }

                    if (file.Offset < 0 || file.Len < 0)
                    {
                        failed++;
                        Console.Error.WriteLine($"[FAIL] Invalid offset/len for file={file.FileName}, offset={file.Offset}, len={file.Len}");
                        continue;
                    }

                    if (file.Len > int.MaxValue)
                    {
                        failed++;
                        Console.Error.WriteLine($"[FAIL] File too large for current extractor (>2GB): {file.FileName}");
                        continue;
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
                        continue;
                    }

                    if (file.BUseEncrypt)
                    {
                        var nonce = new byte[12];
                        BinaryPrimitives.WriteInt32LittleEndian(nonce.AsSpan(0, 4), parsed.Version);
                        BinaryPrimitives.WriteInt64LittleEndian(nonce.AsSpan(4, 8), file.IvSeed);
                        payload = ChaCha20Cipher.Decrypt(key, nonce, 1, payload);
                    }

                    var safeRelative = CliHelpers.BuildSafeRelativePath(file.FileName);
                    if (string.IsNullOrWhiteSpace(safeRelative))
                    {
                        failed++;
                        Console.Error.WriteLine($"[FAIL] Invalid output file path from virtual name: {file.FileName}");
                        continue;
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
            }
        }
        finally
        {
            foreach (var stream in chkStreamCache.Values)
                stream.Dispose();
        }

        Console.WriteLine("[STEP 6/6] Finished extraction.");
        Console.WriteLine($"Done. Success={success}, Failed={failed}, MissingChk={missingChk}, ChkFromPersistent={fromPersistent}, ChkFromStreaming={fromStreaming}");
        return failed == 0 ? 0 : 1;
    }
}
