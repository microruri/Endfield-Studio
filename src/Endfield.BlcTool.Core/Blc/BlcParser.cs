using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Endfield.BlcTool.Core.Models;

namespace Endfield.BlcTool.Core.Blc;

/// <summary>
/// Parses decrypted Endfield .blc bytes into structured metadata.
/// The binary layout is inferred from known game resources.
/// </summary>
public static class BlcParser
{
    /// <summary>
    /// Parse entire .blc metadata payload.
    /// </summary>
    public static BlcMainInfo Parse(byte[] decryptedData)
    {
        if (decryptedData == null)
            throw new ArgumentNullException(nameof(decryptedData));

        using var ms = new MemoryStream(decryptedData);
        using var br = new BinaryReader(ms);

        var main = new BlcMainInfo
        {
            // Metadata format version.
            Version = br.ReadInt32()
        };

        // Skip nonce area retained in decrypted buffer layout.
        br.BaseStream.Seek(12, SeekOrigin.Current);
        main.GroupCfgName = ReadVfsString(br);
        main.GroupCfgHashName = ReadBEInt64Hex(br);
        main.GroupFileInfoNum = br.ReadInt32();
        main.GroupChunksLength = br.ReadInt64();
        main.BlockType = br.ReadByte();

        var chunkCount = br.ReadInt32();
        main.AllChunks = new List<BlcChunkInfo>(Math.Max(0, chunkCount));
        // Chunk table: each chunk references one .chk and contains file entries.
        for (var i = 0; i < chunkCount; i++)
        {
            var chunk = new BlcChunkInfo
            {
                Md5Name = ReadBEUInt128Hex(br),
                ContentMD5 = ReadBEUInt128Hex(br),
                Length = br.ReadInt64(),
                BlockType = br.ReadByte()
            };

            var fileCount = br.ReadInt32();
            chunk.Files = new List<BlcFileInfo>(Math.Max(0, fileCount));
            // File records define how to locate bytes inside target .chk chunks.
            for (var j = 0; j < fileCount; j++)
            {
                var file = new BlcFileInfo
                {
                    FileName = ReadVfsString(br),
                    FileNameHash = ReadBEInt64Hex(br),
                    FileChunkMD5Name = ReadBEUInt128Hex(br),
                    FileDataMD5 = ReadBEUInt128Hex(br),
                    Offset = br.ReadInt64(),
                    Len = br.ReadInt64(),
                    BlockType = br.ReadByte(),
                    BUseEncrypt = br.ReadBoolean()
                };

                if (file.BUseEncrypt)
                    file.IvSeed = br.ReadInt64();

                chunk.Files.Add(file);
            }

            main.AllChunks.Add(chunk);
        }

        return main;
    }

    private static string ReadVfsString(BinaryReader br)
    {
        // VFS strings are stored as: [int16 length][utf8 bytes].
        var len = br.ReadInt16();
        if (len <= 0)
            return string.Empty;

        var bytes = br.ReadBytes(len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string ReadBEInt64Hex(BinaryReader br)
    {
        // Read 64-bit value that is encoded in big-endian byte order.
        // Returned as compact upper hex text for easier comparison with tooling output.
        var data = br.ReadBytes(8);
        Array.Reverse(data);

        var start = 0;
        while (start < data.Length && (data[start] == 0x00 || data[start] == 0xFF))
            start++;

        if (start == data.Length)
            return "00";

        var trimmed = new byte[data.Length - start];
        Buffer.BlockCopy(data, start, trimmed, 0, trimmed.Length);
        Array.Reverse(trimmed);
        return Convert.ToHexString(trimmed);
    }

    private static string ReadBEUInt128Hex(BinaryReader br)
    {
        // 128-bit identifiers are stored as two 64-bit big-endian segments.
        // Keep hex in the same order as source stream for stable chunk/file id matching.
        var high = br.ReadBytes(8);
        var low = br.ReadBytes(8);
        return Convert.ToHexString(high) + Convert.ToHexString(low);
    }
}
