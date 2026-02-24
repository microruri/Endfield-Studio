namespace Endfield.Tool.GUI.Services;

public static class GameCatalogLayout
{
    public const string DataFolderName = "Endfield_Data";

    public const string PersistentFolderName = "Persistent";
    public const string StreamingAssetsFolderName = "StreamingAssets";

    public const string InitialIndexFileName = "index_initial.json";
    public const string MainIndexFileName = "index_main.json";

    public static readonly string[] IndexFileNames =
    {
        InitialIndexFileName,
        MainIndexFileName
    };

    public static readonly string[] SourceFolders =
    {
        PersistentFolderName,
        StreamingAssetsFolderName
    };

    public static readonly string[] JsonDataRelativePaths =
    {
        "Persistent/index_initial.json",
        "Persistent/index_main.json",
        "Persistent/pref_initial.json",
        "Persistent/pref_main.json",
        "StreamingAssets/index_initial.json",
        "StreamingAssets/index_main.json",
        "StreamingAssets/pref_initial.json",
        "StreamingAssets/pref_main.json"
    };
}
