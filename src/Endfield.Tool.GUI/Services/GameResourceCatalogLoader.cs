using System.Text.Json;
using Endfield.JsonTool.Core.Json;

namespace Endfield.Tool.GUI.Services;

public static class GameResourceCatalogLoader
{
    private const string DataFolderName = "Endfield_Data";

    private static readonly string[] IndexFileNames =
    {
        "index_initial.json",
        "index_main.json"
    };

    private static readonly string[] SourceFolders =
    {
        "Persistent",
        "StreamingAssets"
    };

    public static bool TryLoad(string gameRoot, out List<ResourceCatalogEntry> entries, out string error)
    {
        entries = new List<ResourceCatalogEntry>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            error = "Game root directory does not exist.";
            return false;
        }

        var dataFolder = Path.Combine(gameRoot, DataFolderName);
        if (!Directory.Exists(dataFolder))
        {
            error = "Endfield_Data folder is missing.";
            return false;
        }

        var loadErrors = new List<string>();

        foreach (var sourceFolder in SourceFolders)
        {
            foreach (var indexFile in IndexFileNames)
            {
                var indexPath = Path.Combine(dataFolder, sourceFolder, indexFile);
                if (!File.Exists(indexPath))
                    continue;

                if (TryParseIndex(indexPath, sourceFolder, indexFile, out var loadedEntries, out var parseError))
                {
                    entries.AddRange(loadedEntries);
                }
                else
                {
                    loadErrors.Add($"{indexFile} ({sourceFolder}): {parseError}");
                }
            }
        }

        if (entries.Count == 0)
        {
            error = loadErrors.Count == 0
                ? "No resource index entries were found."
                : string.Join(Environment.NewLine, loadErrors);
            return false;
        }

        entries = entries
            .OrderBy(x => x.VirtualPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.IndexFile, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }

    private static bool TryParseIndex(
        string indexPath,
        string sourceFolder,
        string indexFile,
        out List<ResourceCatalogEntry> entries,
        out string error)
    {
        entries = new List<ResourceCatalogEntry>();
        error = string.Empty;

        try
        {
            var encryptedBytes = File.ReadAllBytes(indexPath);
            var firstStageBytes = JsonDecryptor.DecryptFirstStage(encryptedBytes);

            if (!JsonDecryptor.TryDecodeUtf8Json(firstStageBytes, out var jsonText))
            {
                error = "index is not valid UTF-8 JSON";
                return false;
            }

            using var doc = JsonDocument.Parse(jsonText);
            if (!doc.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                error = "index is missing files array";
                return false;
            }

            foreach (var fileNode in files.EnumerateArray())
            {
                var name = fileNode.TryGetProperty("name", out var nameNode) && nameNode.ValueKind == JsonValueKind.String
                    ? nameNode.GetString() ?? string.Empty
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var type = fileNode.TryGetProperty("type", out var typeNode) && typeNode.TryGetInt32(out var parsedType)
                    ? parsedType
                    : -1;

                var size = fileNode.TryGetProperty("size", out var sizeNode) && sizeNode.TryGetInt64(out var parsedSize)
                    ? parsedSize
                    : 0;

                entries.Add(new ResourceCatalogEntry(name, type, size, indexFile, sourceFolder));
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
