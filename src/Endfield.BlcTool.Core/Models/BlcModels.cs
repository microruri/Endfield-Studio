using System.Collections.Generic;

namespace Endfield.BlcTool.Core.Models;

/// <summary>
/// Top-level metadata object parsed from one decrypted .blc file.
/// </summary>
public sealed class BlcMainInfo
{
    /// <summary>
    /// Format version stored in the metadata header.
    /// </summary>
    public int Version { get; set; }
    /// <summary>
    /// Logical group configuration file path/name.
    /// </summary>
    public string GroupCfgName { get; set; } = string.Empty;
    /// <summary>
    /// Hash-like identifier of GroupCfgName in hex text form.
    /// </summary>
    public string GroupCfgHashName { get; set; } = string.Empty;
    /// <summary>
    /// Number of file-info entries declared by the group metadata.
    /// </summary>
    public int GroupFileInfoNum { get; set; }
    /// <summary>
    /// Total byte length indicator for chunk table area.
    /// </summary>
    public long GroupChunksLength { get; set; }
    /// <summary>
    /// Block type marker used by the game for this metadata set.
    /// </summary>
    public byte BlockType { get; set; }
    /// <summary>
    /// All chunk records; each chunk usually maps to one .chk container.
    /// </summary>
    public List<BlcChunkInfo> AllChunks { get; set; } = new();
}

/// <summary>
/// One chunk entry from .blc metadata.
/// </summary>
public sealed class BlcChunkInfo
{
    /// <summary>
    /// Chunk identifier, typically matching the corresponding .chk filename stem.
    /// </summary>
    public string Md5Name { get; set; } = string.Empty;
    /// <summary>
    /// Chunk content hash value in hex text form.
    /// </summary>
    public string ContentMD5 { get; set; } = string.Empty;
    /// <summary>
    /// Raw chunk length metadata value.
    /// </summary>
    public long Length { get; set; }
    /// <summary>
    /// Chunk-level block type marker.
    /// </summary>
    public byte BlockType { get; set; }
    /// <summary>
    /// Files indexed inside this chunk.
    /// </summary>
    public List<BlcFileInfo> Files { get; set; } = new();
}

/// <summary>
/// One file-index record from .blc metadata.
/// </summary>
public sealed class BlcFileInfo
{
    /// <summary>
    /// Original virtual file path/name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>
    /// Hash-like identifier of FileName in hex text form.
    /// </summary>
    public string FileNameHash { get; set; } = string.Empty;
    /// <summary>
    /// Target chunk id this file belongs to.
    /// </summary>
    public string FileChunkMD5Name { get; set; } = string.Empty;
    /// <summary>
    /// File content hash in hex text form.
    /// </summary>
    public string FileDataMD5 { get; set; } = string.Empty;
    /// <summary>
    /// Byte offset within the target .chk file.
    /// </summary>
    public long Offset { get; set; }
    /// <summary>
    /// Byte length within the target .chk file.
    /// </summary>
    public long Len { get; set; }
    /// <summary>
    /// File-level block type marker.
    /// </summary>
    public byte BlockType { get; set; }
    /// <summary>
    /// Whether this file segment requires extra decryption when reading from .chk.
    /// </summary>
    public bool BUseEncrypt { get; set; }
    /// <summary>
    /// IV seed used for per-file decryption if <see cref="BUseEncrypt"/> is true.
    /// </summary>
    public long IvSeed { get; set; }
}
