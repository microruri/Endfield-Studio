using System.Collections.Generic;

namespace Endfield.Tool.CLI.Models;

/// <summary>
/// Top-level model for decrypted index_initial.json.
/// </summary>
public sealed class IndexInitialModel
{
    /// <summary>
    /// Whether this index is marked as initial.
    /// </summary>
    public bool IsInitial { get; set; }

    /// <summary>
    /// File entries listed in the initial index.
    /// </summary>
    public List<IndexFileEntry> Files { get; set; } = new();
}

/// <summary>
/// One file record from index_initial.json.
/// </summary>
public sealed class IndexFileEntry
{
    /// <summary>
    /// Entry index in source json.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Relative virtual file path.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Numeric resource type id.
    /// </summary>
    public int Type { get; set; }

    /// <summary>
    /// Byte size from index metadata.
    /// </summary>
    public long Size { get; set; }
}
