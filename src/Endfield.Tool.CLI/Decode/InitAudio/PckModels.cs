using System.Collections.Generic;

namespace Endfield.Tool.CLI.Decode.InitAudio;

/// <summary>
/// Parsed PCK package metadata.
/// </summary>
public sealed class PckParseResult
{
    public PckHeader Header { get; set; } = new();
    public bool HeaderWasDeciphered { get; set; }
    public int HeaderDecipherOffset { get; set; }
    public int HeaderDecipherLength { get; set; }
    public int DataOffset { get; set; }
    public List<PckSectionInfo> Sections { get; set; } = new();
    public List<PckEntryInfo> Entries { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// PCK fixed header fields.
/// </summary>
public sealed class PckHeader
{
    public uint Magic { get; set; }
    public uint Seed { get; set; }
    public uint Padding { get; set; }
    public uint SizeFolder { get; set; }
    public uint SizeA { get; set; }
    public uint SizeB { get; set; }
    public uint SizeC { get; set; }
}

/// <summary>
/// One logical section in PCK header payload.
/// </summary>
public sealed class PckSectionInfo
{
    public string Name { get; set; } = string.Empty;
    public int Offset { get; set; }
    public int Length { get; set; }
    public int EntryCountDeclared { get; set; }
    public int EntryCountParsed { get; set; }
    public bool Prefer64BitEntries { get; set; }
    public int ParsingCursorEnd { get; set; }
}

/// <summary>
/// One unpackable PCK entry.
/// </summary>
public sealed class PckEntryInfo
{
    public int GlobalIndex { get; set; }
    public string SectionName { get; set; } = string.Empty;
    public int SectionEntryIndex { get; set; }
    public bool Is64BitId { get; set; }
    public ulong Id { get; set; }
    public uint IdLow { get; set; }
    public uint IdHigh { get; set; }
    public uint Flag1 { get; set; }
    public uint Flag2 { get; set; }
    public uint Size { get; set; }
    public uint Offset { get; set; }
    public int RawEntryOffset { get; set; }
    public int RawEntryLength { get; set; }
    public bool OffsetSizeInRange { get; set; }
}
