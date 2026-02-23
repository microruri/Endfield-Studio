using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Endfield.BlcTool.Core.Blc;
using Endfield.BlcTool.Core.Crypto;
using Endfield.BlcTool.Core.Models;
using Endfield.Cli.App;

namespace Endfield.Cli.Operations;

/// <summary>
/// Extracts files whose FileName matches a filter from one specified .chk file.
/// Matching is based on virtual FileName only (case-insensitive substring).
/// </summary>
public static class ExtractFromChkOperation
{
    public static int Execute(string gameRoot, string chkPath, string fileNameFilter, string outputPath)
    {
        Console.WriteLine("[STEP 1/5] Validate input and resolve chk path...");
        var fullChkPath = Path.GetFullPath(chkPath);
        if (!File.Exists(fullChkPath))
        {
            Console.Error.WriteLine($"[FAIL] .chk file not found: {fullChkPath}");
            return 2;
        }

        var chkDir = Path.GetDirectoryName(fullChkPath);
        if (string.IsNullOrWhiteSpace(chkDir))
        {
            Console.Error.WriteLine("[FAIL] Invalid .chk path.");
            return 2;
        }

        var groupHash = new DirectoryInfo(chkDir).Name;
        var chunkId = Path.GetFileNameWithoutExtension(fullChkPath);
        var filter = fileNameFilter.Trim();
        if (string.IsNullOrWhiteSpace(filter))
        {
            Console.Error.WriteLine("[FAIL] Filter cannot be empty.");
            return 2;
        }

        Console.WriteLine($"[INFO] GroupHash={groupHash}, ChunkId={chunkId}, Filter={filter}");

        Console.WriteLine("[STEP 2/5] Locate matching .blc metadata source...");
        var persistentBlc = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "VFS", groupHash, $"{groupHash}.blc");
        var streamingBlc = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "VFS", groupHash, $"{groupHash}.blc");
        var selectedBlc = CliHelpers.ResolvePreferredPath(persistentBlc, streamingBlc);
        if (selectedBlc == null)
        {
            Console.Error.WriteLine("[FAIL] Related .blc file not found.");
            Console.Error.WriteLine($"  - {persistentBlc}");
            Console.Error.WriteLine($"  - {streamingBlc}");
            return 3;
        }

        Console.WriteLine($"[INFO] Selected .blc: {selectedBlc}");

        Console.WriteLine("[STEP 3/5] Parse .blc and filter file entries in target chunk...");
        var blc = BlcDecoder.Decode(File.ReadAllBytes(selectedBlc));
        var candidates = blc.AllChunks
            .SelectMany(c => c.Files)
            .Where(f => string.Equals(f.FileChunkMD5Name, chunkId, StringComparison.OrdinalIgnoreCase))
            .Where(f => f.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Console.WriteLine($"[INFO] Candidate files in chunk: {candidates.Count}");
        if (candidates.Count == 0)
        {
            Console.WriteLine("[DONE] No file matched filter in this .chk.");
            return 0;
        }

        Console.WriteLine("[STEP 4/5] Extract matched files from .chk...");
        Directory.CreateDirectory(outputPath);

        var key = KeyDeriver.GetCommonChachaKey();
        var success = 0;
        var failed = 0;

        using (var chkStream = File.Open(fullChkPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            foreach (var file in candidates)
            {
                try
                {
                    if (file.Offset < 0 || file.Len < 0 || file.Len > int.MaxValue)
                    {
                        failed++;
                        Console.Error.WriteLine($"[FAIL] Invalid offset/len: {file.FileName}");
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
                        Console.Error.WriteLine($"[FAIL] Short read: {file.FileName}, expected={length}, actual={totalRead}");
                        continue;
                    }

                    if (file.BUseEncrypt)
                    {
                        var nonce = new byte[12];
                        BinaryPrimitives.WriteInt32LittleEndian(nonce.AsSpan(0, 4), blc.Version);
                        BinaryPrimitives.WriteInt64LittleEndian(nonce.AsSpan(4, 8), file.IvSeed);
                        payload = ChaCha20Cipher.Decrypt(key, nonce, 1, payload);
                    }

                    var safeRelative = CliHelpers.BuildSafeRelativePath(file.FileName);
                    if (string.IsNullOrWhiteSpace(safeRelative))
                    {
                        failed++;
                        Console.Error.WriteLine($"[FAIL] Invalid output path from file name: {file.FileName}");
                        continue;
                    }

                    var outFile = Path.Combine(outputPath, safeRelative);
                    var outDir = Path.GetDirectoryName(outFile);
                    if (!string.IsNullOrWhiteSpace(outDir))
                        Directory.CreateDirectory(outDir);

                    File.WriteAllBytes(outFile, payload);
                    success++;
                    Console.WriteLine($"[OK] {file.FileName} -> {outFile} (offset={file.Offset}, len={file.Len}, encrypted={file.BUseEncrypt})");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"[FAIL] {file.FileName}: {ex.Message}");
                }
            }
        }

        Console.WriteLine("[STEP 5/5] Finished.");
        Console.WriteLine($"Done. Matched={candidates.Count}, Success={success}, Failed={failed}");
        return failed == 0 ? 0 : 1;
    }
}
