namespace Endfield.Tool.GUI.Services;

public sealed record ResourceCatalogEntry(
    string VirtualPath,
    int Type,
    long Size,
    string IndexFile,
    string SourceFolder);
