using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Endfield.Cli.Extract;

/// <summary>
/// Decoder for Endfield manifest.hgmmap binary payload.
/// Format and decoding flow are aligned with known community tooling.
/// </summary>
public static class ManifestDecoder
{
    private const uint Head1 = 0xFF11FF11;
    private const uint Head2 = 0xF1F2F3F4;

    public static ManifestScheme Decode(byte[] encoded)
    {
        if (encoded == null)
            throw new ArgumentNullException(nameof(encoded));

        var data = DecompressBrotli(encoded);
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        var scheme = new ManifestScheme();
        if (br.ReadUInt32() != Head1)
            throw new InvalidDataException("Invalid manifest HEAD1.");

        scheme.Version = ReadLenUnicodeString(br);

        if (br.ReadUInt32() != Head2)
            throw new InvalidDataException("Invalid manifest HEAD2.");

        scheme.Hash = ReadLenUnicodeString(br);
        scheme.PerforceCL = ReadLenUnicodeString(br);

        var assetInfoSize = br.ReadInt32();
        scheme.AssetInfoAddress = br.BaseStream.Position;
        br.BaseStream.Seek(assetInfoSize, SeekOrigin.Current);

        var bundleSize = br.ReadInt32();
        scheme.BundleAddress = br.BaseStream.Position;
        br.BaseStream.Seek(bundleSize, SeekOrigin.Current);

        var bundleArraySize = br.ReadInt32();
        scheme.BundleArrayAddress = br.BaseStream.Position;
        br.BaseStream.Seek(bundleArraySize, SeekOrigin.Current);

        scheme.DataAddress = br.BaseStream.Position + 4;

        br.BaseStream.Position = scheme.BundleArrayAddress;
        var bundleCount = br.ReadInt32();
        for (var i = 0; i < bundleCount; i++)
        {
            var bundle = new ManifestBundle
            {
                BundleIndex = br.ReadInt32()
            };

            var nameOffset = br.ReadInt32();
            var depsOffset = br.ReadInt32();
            var revDepsOffset = br.ReadInt32();
            var dirDepsOffset = br.ReadInt32();
            bundle.BundleFlags = br.ReadInt32();
            bundle.HashName = br.ReadInt64();
            bundle.HashVersion = br.ReadInt64();
            bundle.Category = (byte)br.ReadInt32();
            br.ReadInt32(); // padding

            var currPos = br.BaseStream.Position;
            bundle.Name = ReadStringAt(br, scheme.DataAddress, nameOffset);
            bundle.Dependencies = ReadIntArrayAt(br, scheme.DataAddress, depsOffset);
            bundle.DirectReverseDependencies = ReadIntArrayAt(br, scheme.DataAddress, revDepsOffset);
            bundle.DirectDependencies = ReadIntArrayAt(br, scheme.DataAddress, dirDepsOffset);
            scheme.Bundles.Add(bundle);
            br.BaseStream.Position = currPos;
        }

        br.BaseStream.Seek(scheme.AssetInfoAddress, SeekOrigin.Begin);
        var assetCapacity = br.ReadInt32();
        br.BaseStream.Seek(assetCapacity * 8, SeekOrigin.Current);

        var assetCount = (int)((scheme.AssetInfoAddress + assetInfoSize) - br.BaseStream.Position) / 24;
        for (var i = 0; i < assetCount; i++)
        {
            var info = new ManifestAssetInfo
            {
                PathHashHead = br.ReadInt64()
            };

            var pathOffset = br.ReadInt32();
            info.BundleIndex = br.ReadInt32();
            info.AssetSize = br.ReadInt32();
            br.ReadInt32();

            info.Path = ReadCompressStringAt(br, scheme.DataAddress, pathOffset);
            scheme.Assets.Add(info);
        }

        return scheme;
    }

    private static string ReadLenUnicodeString(BinaryReader br)
    {
        var len = br.ReadInt32();
        return Encoding.Unicode.GetString(br.ReadBytes(len * 2));
    }

    private static int[] ReadIntArrayAt(BinaryReader br, long dataPos, int offset)
    {
        var savedPos = br.BaseStream.Position;
        br.BaseStream.Position = dataPos + offset;
        var count = br.ReadInt32();
        var result = new int[count];
        for (var i = 0; i < count; i++)
            result[i] = br.ReadInt32();

        br.BaseStream.Position = savedPos;
        return result;
    }

    private static string ReadStringAt(BinaryReader br, long dataPos, int offset)
    {
        var savedPos = br.BaseStream.Position;
        br.BaseStream.Position = dataPos + offset;

        var len = br.ReadInt32();
        var result = Encoding.Unicode.GetString(br.ReadBytes(len));
        br.BaseStream.Position = savedPos;
        return result;
    }

    private static string ReadCompressStringAt(BinaryReader br, long dataPos, int offset)
    {
        var savedPos = br.BaseStream.Position;
        br.BaseStream.Position = dataPos + offset;
        var compressedLen = br.ReadInt32();
        var rawData = br.ReadBytes(compressedLen);
        br.BaseStream.Position = savedPos;

        var data = DecompressBrotli(rawData);
        return Encoding.Unicode.GetString(data);
    }

    private static byte[] DecompressBrotli(byte[] compressed)
    {
        using var ms = new MemoryStream(compressed);
        using var brotli = new BrotliStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        brotli.CopyTo(outMs);
        return outMs.ToArray();
    }
}
