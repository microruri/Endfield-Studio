using System.Text.Json;
using Endfield.JsonTool.Core.Json;

namespace Endfield.Tool.GUI.Services;

public static class EncryptedIndexParser
{
    public static bool TryParseFiles(string indexPath, out List<CatalogFileDescriptor> files, out string error)
    {
        files = new List<CatalogFileDescriptor>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(indexPath))
        {
            error = "index path is required";
            return false;
        }

        if (!File.Exists(indexPath))
        {
            error = $"index file does not exist: {indexPath}";
            return false;
        }

        try
        {
            // Endfield index files are encrypted and require first-stage decryption
            // before their JSON payload can be parsed.
            var encryptedBytes = File.ReadAllBytes(indexPath);
            var firstStageBytes = JsonDecryptor.DecryptFirstStage(encryptedBytes);

            if (!JsonDecryptor.TryDecodeUtf8Json(firstStageBytes, out var jsonText))
            {
                error = "index is not valid UTF-8 JSON";
                return false;
            }

            using var doc = JsonDocument.Parse(jsonText);
            if (!doc.RootElement.TryGetProperty("files", out var filesNode) || filesNode.ValueKind != JsonValueKind.Array)
            {
                error = "index is missing files array";
                return false;
            }

            foreach (var fileNode in filesNode.EnumerateArray())
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

                files.Add(new CatalogFileDescriptor(name, type, size));
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

public sealed record CatalogFileDescriptor(string Name, int Type, long Size);
