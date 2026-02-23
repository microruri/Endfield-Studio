using System;
using System.IO;
using System.Linq;
using Endfield.BlcTool.Core.Blc;
using Endfield.Cli.App;

namespace Endfield.Cli.Operations;

/// <summary>
/// Resolves and prints required .chk files for one resource type.
/// </summary>
public static class ChkListOperation
{
    public static int Execute(string gameRoot, string resourceTypeName)
    {
        Console.WriteLine("[STEP 1/4] Resolve requested resource type...");
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

        var persistentBlcPath = Path.Combine(persistentVfsRoot, groupHash, $"{groupHash}.blc");
        var streamingBlcPath = Path.Combine(streamingVfsRoot, groupHash, $"{groupHash}.blc");

        Console.WriteLine("[STEP 2/4] Locate source .blc (Persistent first, then StreamingAssets)...");
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
            var selectedChk = CliHelpers.ResolvePreferredPath(persistentChk, streamingChk);

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
}
