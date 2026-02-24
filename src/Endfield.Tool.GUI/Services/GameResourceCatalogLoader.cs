namespace Endfield.Tool.GUI.Services;

public static class GameResourceCatalogLoader
{
    public static bool TryLoad(string gameRoot, out List<ResourceCatalogEntry> entries, out string error)
    {
        entries = new List<ResourceCatalogEntry>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            error = "Game root directory does not exist.";
            return false;
        }

        var dataFolder = Path.Combine(gameRoot, GameCatalogLayout.DataFolderName);
        if (!Directory.Exists(dataFolder))
        {
            error = $"{GameCatalogLayout.DataFolderName} folder is missing.";
            return false;
        }

        var loadErrors = new List<string>();

        foreach (var sourceFolder in GameCatalogLayout.SourceFolders)
        {
            foreach (var indexFile in GameCatalogLayout.IndexFileNames)
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
        out List<ResourceCatalogEntry> catalogEntries,
        out string error)
    {
        catalogEntries = new List<ResourceCatalogEntry>();
        error = string.Empty;

        if (!EncryptedIndexParser.TryParseFiles(indexPath, out var files, out error))
            return false;

        foreach (var file in files)
        {
            catalogEntries.Add(new ResourceCatalogEntry(file.Name, file.Type, file.Size, indexFile, sourceFolder));
        }

        return true;
    }
}
