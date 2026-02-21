using System.Collections.Generic;

namespace Endfield.BlcTool.Core.Models;

public sealed class BlcMainInfo
{
    public int Version { get; set; }
    public string GroupCfgName { get; set; } = string.Empty;
    public string GroupCfgHashName { get; set; } = string.Empty;
    public int GroupFileInfoNum { get; set; }
    public long GroupChunksLength { get; set; }
    public byte BlockType { get; set; }
    public List<BlcChunkInfo> AllChunks { get; set; } = new();
}

public sealed class BlcChunkInfo
{
    public string Md5Name { get; set; } = string.Empty;
    public string ContentMD5 { get; set; } = string.Empty;
    public long Length { get; set; }
    public byte BlockType { get; set; }
    public List<BlcFileInfo> Files { get; set; } = new();
}

public sealed class BlcFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FileNameHash { get; set; } = string.Empty;
    public string FileChunkMD5Name { get; set; } = string.Empty;
    public string FileDataMD5 { get; set; } = string.Empty;
    public long Offset { get; set; }
    public long Len { get; set; }
    public byte BlockType { get; set; }
    public bool BUseEncrypt { get; set; }
    public long IvSeed { get; set; }
}
