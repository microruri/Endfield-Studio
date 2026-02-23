using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Endfield.BlcTool.Core.Blc;
using Endfield.BlcTool.Core.Crypto;
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
    var resourceTypeName = GetOption(args, "-n", "--name");

    if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(operation))
    {
        Console.Error.WriteLine("Missing required options: -g and -t are required.");
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
            "blc-all" => string.IsNullOrWhiteSpace(outputPath)
                ? MissingOptionForOperation("blc-all", "-o/--output")
                : ConvertAllBlc(gameRoot, outputPath),

            "json-index" => string.IsNullOrWhiteSpace(outputPath)
                ? MissingOptionForOperation("json-index", "-o/--output")
                : ConvertIndexJson(gameRoot, outputPath),

            "chk-list" => string.IsNullOrWhiteSpace(resourceTypeName)
                ? MissingOptionForOperation("chk-list", "-n/--name")
                : PrintRequiredChkFiles(gameRoot, resourceTypeName),

            "extract-type" => string.IsNullOrWhiteSpace(resourceTypeName)
                ? MissingOptionForOperation("extract-type", "-n/--name")
                : string.IsNullOrWhiteSpace(outputPath)
                    ? MissingOptionForOperation("extract-type", "-o/--output")
                    : ExtractTypeResources(gameRoot, resourceTypeName, outputPath),

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

static void ProcessBlcFile(string blcPath, string vfsRoot, string outputPath, ref int ok, ref int fail)
{
    try
    {
        var relative = Path.GetRelativePath(vfsRoot, blcPath);
        var bytes = File.ReadAllBytes(blcPath);
        var parsed = BlcDecoder.Decode(bytes);

        var groupName = SanitizeFileName(parsed.GroupCfgName);
        if (string.IsNullOrWhiteSpace(groupName))
            groupName = Path.GetFileNameWithoutExtension(blcPath);

        var outputFile = Path.Combine(outputPath, "blc_groups", $"{groupName}.json");
        var outputDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

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

static int MissingOptionForOperation(string operation, string option)
{
    Console.Error.WriteLine($"Operation '{operation}' requires option {option}.");
    return 2;
}

static string SanitizeFileName(string input)
{
    if (string.IsNullOrWhiteSpace(input))
        return string.Empty;

    var invalidChars = Path.GetInvalidFileNameChars();
    var sb = new StringBuilder(input.Length);
    foreach (var ch in input.Trim())
    {
        sb.Append(Array.IndexOf(invalidChars, ch) >= 0 ? '_' : ch);
    }

    return sb.ToString();
}

static int UnknownOperation(string operation)
{
    Console.Error.WriteLine($"Unknown operation: {operation}");
    Console.Error.WriteLine("Supported operations: blc-all, json-index, chk-list, extract-type");
    return 2;
}

static int ExtractTypeResources(string gameRoot, string resourceTypeName, string outputPath)
{
    Console.WriteLine("[STEP 1/6] Resolve requested resource type...");
    if (!TryGetGroupHashByTypeName(resourceTypeName, out var groupHash, out var canonicalName))
    {
        Console.Error.WriteLine($"Unknown resource type name: {resourceTypeName}");
        Console.Error.WriteLine("Supported names:");
        foreach (var name in GetSupportedTypeNames())
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
    var selectedBlc = ResolvePreferredPath(persistentBlcPath, streamingBlcPath);
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
    var chkRootHints = $"Persistent={persistentDataRoot}, StreamingAssets={streamingDataRoot}";
    Console.WriteLine($"[INFO] CHK roots: {chkRootHints}");

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
                    selectedChk = ResolvePreferredPath(persistentChk, streamingChk);
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

                var safeRelative = BuildSafeRelativePath(file.FileName);
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

static int PrintRequiredChkFiles(string gameRoot, string resourceTypeName)
{
    Console.WriteLine("[STEP 1/4] Resolve requested resource type...");
    if (!TryGetGroupHashByTypeName(resourceTypeName, out var groupHash, out var canonicalName))
    {
        Console.Error.WriteLine($"Unknown resource type name: {resourceTypeName}");
        Console.Error.WriteLine("Supported names:");
        foreach (var name in GetSupportedTypeNames())
            Console.Error.WriteLine($"  - {name}");

        return 2;
    }

    Console.WriteLine($"[INFO] Type: {canonicalName}, GroupHash: {groupHash}");

    var persistentVfsRoot = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "VFS");
    var streamingVfsRoot = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "VFS");

    var persistentBlcPath = Path.Combine(persistentVfsRoot, groupHash, $"{groupHash}.blc");
    var streamingBlcPath = Path.Combine(streamingVfsRoot, groupHash, $"{groupHash}.blc");

    Console.WriteLine("[STEP 2/4] Locate source .blc (Persistent first, then StreamingAssets)...");
    var selectedBlc = ResolvePreferredPath(persistentBlcPath, streamingBlcPath);
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

    Console.WriteLine("[STEP 3/4] Decode .blc and collect required chunk IDs...");
    var parsed = BlcDecoder.Decode(File.ReadAllBytes(selectedBlc));

    var chunkIds = parsed.AllChunks
        .Select(c => c.Md5Name)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToList();

    Console.WriteLine($"[INFO] GroupCfgName={parsed.GroupCfgName}, Chunks={chunkIds.Count}");
    if (chunkIds.Count == 0)
    {
        Console.WriteLine("[DONE] No chunk files required for this type.");
        return 0;
    }

    Console.WriteLine("[STEP 4/4] Resolve required .chk files (Persistent first, then StreamingAssets)...");

    var found = 0;
    var missing = 0;
    var fromPersistent = 0;
    var fromStreaming = 0;

    foreach (var chunkId in chunkIds)
    {
        var relChk = Path.Combine("VFS", groupHash, $"{chunkId}.chk");
        var persistentChk = Path.Combine(gameRoot, "Endfield_Data", "Persistent", relChk);
        var streamingChk = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", relChk);
        var selectedChk = ResolvePreferredPath(persistentChk, streamingChk);

        if (selectedChk == null)
        {
            missing++;
            Console.WriteLine($"[MISS] {relChk}");
            continue;
        }

        var chkSource = selectedChk.StartsWith(Path.Combine(gameRoot, "Endfield_Data", "Persistent"), StringComparison.OrdinalIgnoreCase)
            ? "Persistent"
            : "StreamingAssets";
        if (chkSource == "Persistent")
            fromPersistent++;
        else
            fromStreaming++;

        found++;
        var size = new FileInfo(selectedChk).Length;
        Console.WriteLine($"[CHK] {relChk} | source={chkSource} | size={size}");
    }

    Console.WriteLine($"Done. Required={chunkIds.Count}, Found={found}, Missing={missing}, FromPersistent={fromPersistent}, FromStreaming={fromStreaming}");
    return missing == 0 ? 0 : 1;
}

static IEnumerable<string> GetSupportedTypeNames()
{
    return new[]
    {
        "InitAudio",
        "InitBundle",
        "InitialExtendData",
        "BundleManifest",
        "IFixPatchOut",
        "AuditStreaming",
        "AuditDynamicStreaming",
        "AuditIV",
        "AuditAudio",
        "AuditVideo",
        "Bundle",
        "Audio",
        "Video",
        "IV",
        "Streaming",
        "DynamicStreaming",
        "Lua",
        "Table",
        "JsonData",
        "ExtendData",
        "AudioChinese",
        "AudioEnglish",
        "AudioJapanese",
        "AudioKorean"
    };
}

static bool TryGetGroupHashByTypeName(string resourceTypeName, out string groupHash, out string canonicalName)
{
    var key = resourceTypeName.Trim().ToLowerInvariant();
    (canonicalName, groupHash) = key switch
    {
        "initaudio" => ("InitAudio", "07A1BB91"),
        "initbundle" => ("InitBundle", "0CE8FA57"),
        "initialextenddata" => ("InitialExtendData", "3C9D9D2D"),
        "bundlemanifest" => ("BundleManifest", "1CDDBF1F"),
        "ifixpatchout" => ("IFixPatchOut", "DAFE52C9"),

        "auditstreaming" => ("AuditStreaming", "6432320A"),
        "auditdynamicstreaming" => ("AuditDynamicStreaming", "B9358E30"),
        "auditiv" => ("AuditIV", "06223FE2"),
        "auditaudio" => ("AuditAudio", "1EBAF5C6"),
        "auditvideo" => ("AuditVideo", "2E6CE44D"),

        "bundle" => ("Bundle", "7064D8E2"),
        "audio" => ("Audio", "24ED34CF"),
        "video" => ("Video", "55FC21C6"),
        "iv" => ("IV", "A63D7E6A"),
        "streaming" => ("Streaming", "C3442D43"),
        "dynamicstreaming" => ("DynamicStreaming", "23D53F5D"),
        "lua" => ("Lua", "19E3AE45"),
        "table" => ("Table", "42A8FCA6"),
        "jsondata" => ("JsonData", "775A31D1"),
        "extenddata" => ("ExtendData", "D6E622F7"),

        "audiochinese" => ("AudioChinese", "E1E7D7CE"),
        "audioenglish" => ("AudioEnglish", "A31457D0"),
        "audiojapanese" => ("AudioJapanese", "F668D4EE"),
        "audiokorean" => ("AudioKorean", "E9D31017"),

        _ => (string.Empty, string.Empty)
    };

    return !string.IsNullOrEmpty(groupHash);
}

static string BuildSafeRelativePath(string virtualPath)
{
    if (string.IsNullOrWhiteSpace(virtualPath))
        return string.Empty;

    var normalized = virtualPath.Replace('\\', '/');
    var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
        return string.Empty;

    var invalid = Path.GetInvalidFileNameChars();
    var safeParts = new List<string>(parts.Length);

    foreach (var part in parts)
    {
        if (part == "." || part == "..")
            continue;

        var sb = new StringBuilder(part.Length);
        foreach (var ch in part)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);

        var value = sb.ToString().Trim();
        if (!string.IsNullOrEmpty(value))
            safeParts.Add(value);
    }

    if (safeParts.Count == 0)
        return string.Empty;

    return Path.Combine(safeParts.ToArray());
}

static int ConvertIndexJson(string gameRoot, string outputPath)
{
    var persistentInitial = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "index_initial.json");
    var streamingInitial = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "index_initial.json");
    var persistentMain = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "index_main.json");
    var streamingMain = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "index_main.json");

    var targets = new[]
    {
        (
            Kind: "initial",
            Input: ResolvePreferredPath(persistentInitial, streamingInitial),
            Output: "blc_index_initial.json"
        ),
        (
            Kind: "main",
            Input: ResolvePreferredPath(persistentMain, streamingMain),
            Output: "blc_index_main.json"
        )
    };

    Directory.CreateDirectory(outputPath);

    var ok = 0;
    var fail = 0;
    var missing = 0;

    foreach (var item in targets)
    {
        if (item.Input == null)
        {
            missing++;
            Console.Error.WriteLine($"[MISS] index_{item.Kind}.json not found in Persistent or StreamingAssets.");
            continue;
        }

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
                fail++;
                Console.Error.WriteLine($"[FAIL] {item.Input}: decrypted bytes are not UTF-8 JSON text.");
            }
        }
        catch (Exception ex)
        {
            fail++;
            Console.Error.WriteLine($"[FAIL] {item.Input}: {ex.Message}");
        }
    }

    if (missing == targets.Length)
    {
        Console.Error.WriteLine("No usable index files found. Checked Persistent first, then StreamingAssets.");
        return 3;
    }

    Console.WriteLine($"Done. Success={ok}, Failed={fail}, Missing={missing}");
    return (fail == 0 && missing == 0) ? 0 : 1;
}

static string? ResolvePreferredPath(string preferredPath, string fallbackPath)
{
    if (File.Exists(preferredPath))
        return preferredPath;

    if (File.Exists(fallbackPath))
        return fallbackPath;

    return null;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Endfield.Cli -g <gameRoot> -t <operation> [-o <outputPath>] [-n <resourceTypeName>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -g, --game-root   Game root directory.");
    Console.WriteLine("  -t, --type        Operation type. Supported: blc-all, json-index, chk-list, extract-type");
    Console.WriteLine("  -o, --output      Output directory (required by blc-all/json-index/extract-type).");
    Console.WriteLine("  -n, --name        Resource type name (required by chk-list/extract-type).");
    Console.WriteLine("  -h, --help        Show help.");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t blc-all -o \"out\"");
    Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t chk-list -n AudioChinese");
    Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t extract-type -n Lua -o \"out\\lua\"");
}
