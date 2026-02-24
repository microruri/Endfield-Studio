using System.Text.Json;
using Endfield.JsonTool.Core.Json;

namespace Endfield.Tool.GUI.Services;

public static class GameRootValidator
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

    public static bool TryValidate(string gameRoot, out GameRootValidationResult? result, out string error)
    {
        result = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            error = "Game root directory does not exist.";
            return false;
        }

        var dataFolder = Path.Combine(gameRoot, DataFolderName);
        if (!Directory.Exists(dataFolder))
        {
            error = $"Missing required folder: {DataFolderName}";
            return false;
        }

        foreach (var sourceFolder in SourceFolders)
        {
            foreach (var indexFileName in IndexFileNames)
            {
                var indexPath = Path.Combine(dataFolder, sourceFolder, indexFileName);
                if (!File.Exists(indexPath))
                    continue;

                if (TryReadIndex(indexPath, out error))
                {
                    result = new GameRootValidationResult(gameRoot, indexPath);
                    return true;
                }
            }
        }

        if (string.IsNullOrEmpty(error))
            error = "No readable index_initial.json or index_main.json was found.";

        return false;
    }

    private static bool TryReadIndex(string indexPath, out string error)
    {
        error = string.Empty;

        try
        {
            var encryptedBytes = File.ReadAllBytes(indexPath);
            var firstStageBytes = JsonDecryptor.DecryptFirstStage(encryptedBytes);
            if (!JsonDecryptor.TryDecodeUtf8Json(firstStageBytes, out var plainJson))
            {
                error = $"Index is not valid UTF-8 JSON: {indexPath}";
                return false;
            }

            using var doc = JsonDocument.Parse(plainJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = $"Index JSON root is not an object: {indexPath}";
                return false;
            }

            if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                error = $"Index is missing files array: {indexPath}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to read index: {Path.GetFileName(indexPath)} ({ex.Message})";
            return false;
        }
    }
}
