using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Endfield.BlcTool.Core.Models;

namespace Endfield.BlcTool.Core.Blc;

public static class BlcParser
{
    public static BlcMainInfo Parse(byte[] decryptedData)
    {
        if (decryptedData == null)
            throw new ArgumentNullException(nameof(decryptedData));

        using var ms = new MemoryStream(decryptedData);
        using var br = new BinaryReader(ms);

        var main = new BlcMainInfo
        {
            Version = br.ReadInt32()
        };

        br.BaseStream.Seek(12, SeekOrigin.Current);
        main.GroupCfgName = ReadVfsString(br);
        main.GroupCfgHashName = ReadBEInt64Hex(br);
        main.GroupFileInfoNum = br.ReadInt32();
        main.GroupChunksLength = br.ReadInt64();
        main.BlockType = br.ReadByte();

        var chunkCount = br.ReadInt32();
        main.AllChunks = new List<BlcChunkInfo>(Math.Max(0, chunkCount));
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
        var len = br.ReadInt16();
        if (len <= 0)
            return string.Empty;

        var bytes = br.ReadBytes(len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string ReadBEInt64Hex(BinaryReader br)
    {
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
        var high = br.ReadBytes(8);
        var low = br.ReadBytes(8);
        return Convert.ToHexString(high) + Convert.ToHexString(low);
    }
}
