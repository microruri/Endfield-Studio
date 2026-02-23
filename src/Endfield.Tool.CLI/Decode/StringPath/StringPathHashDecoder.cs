using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Endfield.Tool.CLI.Decode.StringPath;

/// <summary>
/// Decoder for InitStringPathHash.bin/StringPathHash.bin payloads.
/// </summary>
public static class StringPathHashDecoder
{
    /// <summary>
    /// Decodes binary payload into pretty JSON bytes.
    /// </summary>
    public static byte[] DecodeToJsonBytes(byte[] input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        var map = Decode(input);
        return JsonSerializer.SerializeToUtf8Bytes(map, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static Dictionary<long, string> Decode(byte[] data)
    {
        var result = new Dictionary<long, string>();

        using var ms = new MemoryStream(data, false);
        using var br = new BinaryReader(ms);

        var stringPoolOffset = br.ReadInt32();
        var capacity = br.ReadInt32();
        br.ReadBytes(capacity * 8); // skip slots

        var mapList = new List<Mapping>(Math.Max(0, (int)((stringPoolOffset - br.BaseStream.Position) / 16)));
        var nodeCount = (int)((stringPoolOffset - br.BaseStream.Position) / 16);
        for (var i = 0; i < nodeCount; i++)
        {
            var hash = br.ReadInt64();
            var offset = br.ReadInt32();
            br.ReadInt32(); // padding
            mapList.Add(new Mapping(hash, offset));
        }

        foreach (var map in mapList)
        {
            var value = ReadStringAt(br, stringPoolOffset, map.Offset);
            result[map.Hash] = value;
        }

        return result;
    }

    private static string ReadStringAt(BinaryReader br, long dataPos, int offset)
    {
        var savedPos = br.BaseStream.Position;
        br.BaseStream.Position = dataPos + offset;

        var len = br.ReadInt32();
        var value = Encoding.Unicode.GetString(br.ReadBytes(len));

        br.BaseStream.Position = savedPos;
        return value;
    }

    private readonly record struct Mapping(long Hash, int Offset);
}
