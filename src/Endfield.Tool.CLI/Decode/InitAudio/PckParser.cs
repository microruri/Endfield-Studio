using System;
using System.Collections.Generic;

namespace Endfield.Tool.CLI.Decode.InitAudio;

/// <summary>
/// Parser for Endfield Wwise PCK binary layout.
/// </summary>
public static class PckParser
{
    /// <summary>
    /// Parses and returns normalized metadata + header-deciphered bytes.
    /// </summary>
    public static PckParseResult Parse(byte[] sourceData, out byte[] headerDecipheredData)
    {
        if (sourceData == null)
            throw new ArgumentNullException(nameof(sourceData));
        if (sourceData.Length < 28)
            throw new InvalidOperationException("PCK too small to contain fixed header.");

        var data = new byte[sourceData.Length];
        Buffer.BlockCopy(sourceData, 0, data, 0, sourceData.Length);

        var result = new PckParseResult();
        var magic = BitConverter.ToUInt32(data, 0);
        var seed = BitConverter.ToUInt32(data, 4);

        if (PckCipher.IsEncryptedMagic(magic))
        {
            // Decipher header payload: starts at 0x0C, length = seed.
            if (seed > 0 && 12 + seed <= data.Length)
            {
                PckCipher.DecipherInPlace(data, 12, seed, (int)seed);
                data[0] = 0x41;
                data[1] = 0x4B;
                data[2] = 0x50;
                data[3] = 0x4B;
                result.HeaderWasDeciphered = true;
                result.HeaderDecipherOffset = 12;
                result.HeaderDecipherLength = (int)seed;
            }
            else
            {
                result.Warnings.Add("Encrypted magic detected but header decipher range is out-of-file.");
            }
        }
        else if (magic != PckCipher.PlainMagic)
        {
            result.Warnings.Add($"Unexpected PCK magic: 0x{magic:X8}");
        }

        var header = new PckHeader
        {
            Magic = BitConverter.ToUInt32(data, 0),
            Seed = BitConverter.ToUInt32(data, 4),
            Padding = BitConverter.ToUInt32(data, 8),
            SizeFolder = BitConverter.ToUInt32(data, 12),
            SizeA = BitConverter.ToUInt32(data, 16),
            SizeB = BitConverter.ToUInt32(data, 20),
            SizeC = BitConverter.ToUInt32(data, 24)
        };
        result.Header = header;

        var folderOffset = 28;
        var aOffset = folderOffset + (int)header.SizeFolder;
        var bOffset = aOffset + (int)header.SizeA;
        var cOffset = bOffset + (int)header.SizeB;
        var dataOffset = cOffset + (int)header.SizeC;
        result.DataOffset = dataOffset;

        result.Sections.Add(new PckSectionInfo { Name = "Folder", Offset = folderOffset, Length = (int)header.SizeFolder });
        result.Sections.Add(ParseEntrySection(data, "A", aOffset, (int)header.SizeA, prefer64Bit: false, dataOffset, result));
        result.Sections.Add(ParseEntrySection(data, "B", bOffset, (int)header.SizeB, prefer64Bit: false, dataOffset, result));
        result.Sections.Add(ParseEntrySection(data, "C", cOffset, (int)header.SizeC, prefer64Bit: true, dataOffset, result));

        headerDecipheredData = data;
        return result;
    }

    private static PckSectionInfo ParseEntrySection(byte[] data, string name, int sectionOffset, int sectionLength, bool prefer64Bit, int dataOffset, PckParseResult result)
    {
        var section = new PckSectionInfo
        {
            Name = name,
            Offset = sectionOffset,
            Length = sectionLength,
            Prefer64BitEntries = prefer64Bit
        };

        if (sectionLength <= 0)
            return section;

        if (sectionOffset < 0 || sectionOffset + sectionLength > data.Length)
        {
            result.Warnings.Add($"Section {name} is out of file range.");
            return section;
        }

        var cursor = sectionOffset;
        var sectionEnd = sectionOffset + sectionLength;

        if (cursor + 4 > sectionEnd)
        {
            result.Warnings.Add($"Section {name} missing entry count field.");
            return section;
        }

        var declaredCount = BitConverter.ToUInt32(data, cursor);
        cursor += 4;
        section.EntryCountDeclared = (int)declaredCount;

        for (var i = 0; i < declaredCount; i++)
        {
            if (cursor + 20 > sectionEnd)
            {
                result.Warnings.Add($"Section {name} entry[{i}] truncated before 20-byte minimum.");
                break;
            }

            var entryOffset = cursor;
            var w0 = BitConverter.ToUInt32(data, cursor + 0);
            var w1 = BitConverter.ToUInt32(data, cursor + 4);
            var w2 = BitConverter.ToUInt32(data, cursor + 8);
            var w3 = BitConverter.ToUInt32(data, cursor + 12);
            var w4 = BitConverter.ToUInt32(data, cursor + 16);

            var can32 = w1 == 1 && (w4 == 0 || w4 == 1);
            var can64 = cursor + 24 <= sectionEnd && w2 == 1 && (BitConverter.ToUInt32(data, cursor + 20) == 0 || BitConverter.ToUInt32(data, cursor + 20) == 1);

            var use64 = false;
            if (can32 && can64)
                use64 = prefer64Bit;
            else if (can64)
                use64 = true;
            else if (can32)
                use64 = false;
            else
                use64 = prefer64Bit && cursor + 24 <= sectionEnd;

            var entry = new PckEntryInfo
            {
                GlobalIndex = result.Entries.Count,
                SectionName = name,
                SectionEntryIndex = i,
                RawEntryOffset = entryOffset,
                Is64BitId = use64
            };

            if (use64)
            {
                entry.IdLow = w0;
                entry.IdHigh = w1;
                entry.Id = ((ulong)w1 << 32) | w0;
                entry.Flag1 = w2;
                entry.Size = w3;
                entry.Offset = w4;
                entry.Flag2 = BitConverter.ToUInt32(data, cursor + 20);
                entry.RawEntryLength = 24;
                cursor += 24;
            }
            else
            {
                entry.IdLow = w0;
                entry.IdHigh = 0;
                entry.Id = w0;
                entry.Flag1 = w1;
                entry.Size = w2;
                entry.Offset = w3;
                entry.Flag2 = w4;
                entry.RawEntryLength = 20;
                cursor += 20;
            }

            // Entry.Offset is absolute file offset in this title's PCK layout.
            var absoluteDataStart = (long)entry.Offset;
            entry.OffsetSizeInRange = absoluteDataStart >= 0 && absoluteDataStart + entry.Size <= data.Length;
            result.Entries.Add(entry);
            section.EntryCountParsed++;
        }

        section.ParsingCursorEnd = cursor;
        if (cursor < sectionEnd)
            result.Warnings.Add($"Section {name} has {sectionEnd - cursor} trailing bytes after entry parsing.");

        return section;
    }
}
