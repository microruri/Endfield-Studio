namespace Endfield.Tool.GUI.Services;

public static class GameRootValidator
{
    public static bool TryValidate(string gameRoot, out GameRootValidationResult? result, out string error)
    {
        result = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            error = "Game root directory does not exist.";
            return false;
        }

        var dataFolder = Path.Combine(gameRoot, GameCatalogLayout.DataFolderName);
        if (!Directory.Exists(dataFolder))
        {
            error = $"Missing required folder: {GameCatalogLayout.DataFolderName}";
            return false;
        }

        var streamingAssetsFolder = Path.Combine(dataFolder, GameCatalogLayout.StreamingAssetsFolderName);
        if (!Directory.Exists(streamingAssetsFolder))
        {
            error = $"Missing required folder: {GameCatalogLayout.StreamingAssetsFolderName}";
            return false;
        }

        // Validation must be based on StreamingAssets indexes so we do not
        // accidentally accept directories with stale Persistent data only.
        foreach (var indexFileName in GameCatalogLayout.IndexFileNames)
        {
            var indexPath = Path.Combine(streamingAssetsFolder, indexFileName);
            if (!File.Exists(indexPath))
            {
                error = $"Missing required index file: {GameCatalogLayout.StreamingAssetsFolderName}/{indexFileName}";
                return false;
            }

            if (!TryReadIndex(indexPath, out error))
            {
                error = $"Failed to read required index file: {GameCatalogLayout.StreamingAssetsFolderName}/{indexFileName} ({error})";
                return false;
            }
        }

        var validatedIndexPath = Path.Combine(streamingAssetsFolder, GameCatalogLayout.InitialIndexFileName);
        result = new GameRootValidationResult(gameRoot, validatedIndexPath);
        return true;
    }

    private static bool TryReadIndex(string indexPath, out string error)
    {
        if (EncryptedIndexParser.TryParseFiles(indexPath, out _, out error))
            return true;

        if (!string.IsNullOrWhiteSpace(error))
        {
            error = $"Failed to read index: {Path.GetFileName(indexPath)} ({error})";
        }

        return false;
    }
}
