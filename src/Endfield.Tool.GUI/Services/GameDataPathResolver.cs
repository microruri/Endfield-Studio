namespace Endfield.Tool.GUI.Services;

public static class GameDataPathResolver
{
    public const string NotFoundPath = "<not found>";
    private const string UnresolvedSource = "N/A";

    public static List<string> GetExistingJsonDataRelativePaths(string? gameRoot)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(gameRoot))
            return result;

        foreach (var relativePath in GameCatalogLayout.JsonDataRelativePaths)
        {
            if (TryResolveJsonDataAbsolutePath(gameRoot, relativePath, out _))
                result.Add(relativePath);
        }

        return result;
    }

    public static bool TryResolveResourceAbsolutePath(string? gameRoot, ResourceCatalogEntry entry, out string absolutePath)
    {
        absolutePath = NotFoundPath;
        if (string.IsNullOrWhiteSpace(gameRoot))
            return false;

        var primary = Path.Combine(gameRoot, GameCatalogLayout.DataFolderName, entry.SourceFolder, entry.VirtualPath);
        if (File.Exists(primary))
        {
            absolutePath = primary;
            return true;
        }

        var fallbackSource = entry.SourceFolder.Equals(GameCatalogLayout.PersistentFolderName, StringComparison.OrdinalIgnoreCase)
            ? GameCatalogLayout.StreamingAssetsFolderName
            : GameCatalogLayout.PersistentFolderName;

        var fallback = Path.Combine(gameRoot, GameCatalogLayout.DataFolderName, fallbackSource, entry.VirtualPath);
        if (!File.Exists(fallback))
            return false;

        absolutePath = fallback;
        return true;
    }

    public static bool TryResolveJsonDataAbsolutePath(string? gameRoot, string relativePath, out string absolutePath)
    {
        absolutePath = NotFoundPath;
        if (string.IsNullOrWhiteSpace(gameRoot))
            return false;

        var normalized = relativePath.Replace('/', '\\');
        var primary = Path.Combine(gameRoot, GameCatalogLayout.DataFolderName, normalized);
        if (File.Exists(primary))
        {
            absolutePath = primary;
            return true;
        }

        if (normalized.StartsWith($"{GameCatalogLayout.PersistentFolderName}\\", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = normalized[$"{GameCatalogLayout.PersistentFolderName}\\".Length..];
            var fallback = Path.Combine(gameRoot, GameCatalogLayout.DataFolderName, GameCatalogLayout.StreamingAssetsFolderName, suffix);
            if (!File.Exists(fallback))
                return false;

            absolutePath = fallback;
            return true;
        }

        if (normalized.StartsWith($"{GameCatalogLayout.StreamingAssetsFolderName}\\", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = normalized[$"{GameCatalogLayout.StreamingAssetsFolderName}\\".Length..];
            var fallback = Path.Combine(gameRoot, GameCatalogLayout.DataFolderName, GameCatalogLayout.PersistentFolderName, suffix);
            if (!File.Exists(fallback))
                return false;

            absolutePath = fallback;
            return true;
        }

        return false;
    }

    public static string DetectJsonDataSource(string absolutePath)
    {
        if (absolutePath.Contains($"\\{GameCatalogLayout.PersistentFolderName}\\", StringComparison.OrdinalIgnoreCase))
            return GameCatalogLayout.PersistentFolderName;

        if (absolutePath.Contains($"\\{GameCatalogLayout.StreamingAssetsFolderName}\\", StringComparison.OrdinalIgnoreCase))
            return GameCatalogLayout.StreamingAssetsFolderName;

        return UnresolvedSource;
    }
}
