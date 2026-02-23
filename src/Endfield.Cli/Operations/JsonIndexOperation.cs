using System;
using System.IO;
using Endfield.Cli.App;
using Endfield.JsonTool.Core.Json;

namespace Endfield.Cli.Operations;

/// <summary>
/// Decrypts index json files with Persistent-first fallback and writes normalized outputs.
/// </summary>
public static class JsonIndexOperation
{
    public static int Execute(string gameRoot, string outputPath)
    {
        var persistentInitial = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "index_initial.json");
        var streamingInitial = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "index_initial.json");
        var persistentMain = Path.Combine(gameRoot, "Endfield_Data", "Persistent", "index_main.json");
        var streamingMain = Path.Combine(gameRoot, "Endfield_Data", "StreamingAssets", "index_main.json");

        var targets = new[]
        {
            (
                Kind: "initial",
                Input: CliHelpers.ResolvePreferredPath(persistentInitial, streamingInitial),
                Output: "blc_index_initial.json"
            ),
            (
                Kind: "main",
                Input: CliHelpers.ResolvePreferredPath(persistentMain, streamingMain),
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
}
