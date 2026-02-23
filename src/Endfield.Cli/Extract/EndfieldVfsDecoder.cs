using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Endfield.Cli.Extract;

/// <summary>
/// Decoder for Endfield-encrypted UnityFS containers (VFS wrapper format).
/// Produces a plain UnityFS byte stream that can be inspected by external tools.
///
/// This implementation follows the Endfield VFS scramble/descramble layout:
/// - 8-byte validation header
/// - scrambled container header fields
/// - encrypted blocks info + directory info section
/// - per-block encrypted + LZ4Inv compressed payload blocks
/// </summary>
public static class EndfieldVfsDecoder
{
    private const uint HeaderMask = 0x4A92F0CD;
    private const uint HeaderCheckMask = 0xD8B1E637;
    private const uint HeaderFieldMask32 = 0xF74324EE;
    private const ulong HeaderFieldMask64 = 0xA4F1A11747816520UL;

    private const uint BlocksCountMask = 0x91CE0A4F;
    private const uint NodesCountMask = 0xE4C1D9F2;

    private const ushort BlockFlagsXor = 0x9CD6;
    private const ushort BlockFlagsMask = 0x523F;
    private const uint NodeCountXor = 0x5DE50A6B;

    private static readonly byte[] VfsSBox =
    {
        0xE4, 0xB9, 0x45, 0x07, 0x92, 0x82, 0x2F, 0x43, 0xF5, 0xC9, 0x22, 0x25, 0xA9, 0x4F, 0x46, 0x6D,
        0x4A, 0x71, 0x8B, 0x6C, 0x8C, 0xEB, 0xB2, 0xAC, 0xCF, 0x0C, 0x9E, 0x01, 0x38, 0x32, 0xD3, 0x93,
        0x98, 0x63, 0xDA, 0x96, 0xE5, 0xC4, 0xC3, 0x6B, 0x7F, 0x26, 0x72, 0xD7, 0x97, 0xD5, 0x80, 0xBC,
        0x5D, 0xBB, 0x55, 0x67, 0x10, 0x73, 0xB3, 0x8D, 0xE2, 0x35, 0x29, 0x47, 0xA8, 0x60, 0x3F, 0xC5,
        0xEF, 0x68, 0xEC, 0xBE, 0xAB, 0xC6, 0xB8, 0x5C, 0xD8, 0x15, 0x09, 0x54, 0xF3, 0x7A, 0x40, 0xA2,
        0x30, 0x0A, 0xDC, 0x53, 0xFA, 0xDB, 0xF1, 0x78, 0xDE, 0xAD, 0xF0, 0xB5, 0xC1, 0x81, 0x9F, 0x3E,
        0x83, 0x90, 0x31, 0xF2, 0xFB, 0x21, 0x28, 0x85, 0x06, 0xCA, 0xCD, 0x1E, 0xD4, 0x3C, 0xA0, 0xC8,
        0x23, 0x16, 0x6E, 0x89, 0x1D, 0xE7, 0xEE, 0x5E, 0x42, 0xBD, 0xCB, 0x13, 0x50, 0xA6, 0x4E, 0x49,
        0x58, 0xDF, 0x2C, 0x84, 0x87, 0xB6, 0x91, 0x52, 0xDD, 0x19, 0xF9, 0x2B, 0x4D, 0x77, 0xBA, 0x04,
        0xA5, 0x41, 0xCE, 0x94, 0x3D, 0x5F, 0xFC, 0x9B, 0x79, 0x9A, 0x7E, 0x65, 0x5A, 0xB1, 0x66, 0x34,
        0x56, 0xA7, 0x1A, 0xBF, 0xEA, 0x7D, 0x27, 0x0B, 0x59, 0x2E, 0xAE, 0x14, 0x33, 0xC0, 0x51, 0x39,
        0xC7, 0x3A, 0x2A, 0x9D, 0xF4, 0x7C, 0xCC, 0xD1, 0xD6, 0x70, 0x37, 0x0E, 0x75, 0x02, 0x1B, 0xE3,
        0xE9, 0x48, 0x0D, 0x24, 0x2D, 0xF7, 0xD2, 0xB7, 0xAF, 0xA3, 0xA1, 0x64, 0x7B, 0xED, 0xF8, 0x05,
        0x95, 0x3B, 0x74, 0xFD, 0x62, 0xD0, 0x0F, 0xFF, 0x4B, 0xAA, 0x88, 0x5B, 0x03, 0xB4, 0xE8, 0x9C,
        0xB0, 0x17, 0x1C, 0x76, 0x57, 0xE0, 0xA4, 0x44, 0x20, 0xD9, 0x8E, 0x11, 0x86, 0x69, 0x36, 0xFE,
        0x4C, 0x6F, 0x61, 0x6A, 0x8F, 0xE1, 0x18, 0x8A, 0x12, 0x99, 0xE6, 0x1F, 0x00, 0x08, 0xF6, 0xC2
    };

    private static readonly byte[] VfsAesKey = { 0x3A, 0xF1, 0x8C, 0x47, 0xB2, 0x09, 0x6D, 0xEE, 0x51, 0x24, 0x90, 0x7C, 0x18, 0xD3, 0xA4, 0x62 };
    private static readonly byte[] VfsAesIv = { 0xC7, 0x12, 0x5E, 0xA9, 0x04, 0xDB, 0x33, 0x88, 0xF2, 0x0E, 0x77, 0x49, 0x65, 0xBA, 0x1C, 0x93 };
    private const ulong VfsAesXorKey = 0xF19AB7752CDD0196UL;

    public static EndfieldVfsDecodeResult DecodeToUnityFs(byte[] input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        using var source = new MemoryStream(input, false);
        using var reader = new BinaryReader(source);

        ValidateHeader(reader);
        var header = ReadHeader(reader);

        if ((header.Flags & 0x80u) != 0)
            throw new InvalidDataException("BlocksInfoAtTheEnd mode is not supported for this operation.");

        var blockInfosOffset = header.EncFlags >= 7 ? 48 : 40;
        if (blockInfosOffset + header.CompressedBlocksInfoSize > input.Length)
            throw new InvalidDataException("Invalid blocks info range.");

        var blocksInfoBytes = new byte[header.CompressedBlocksInfoSize];
        Buffer.BlockCopy(input, blockInfosOffset, blocksInfoBytes, 0, blocksInfoBytes.Length);

        byte[] blocksInfoRaw;
        var blocksInfoCompressionType = (int)(header.Flags & 0x3F);
        if (blocksInfoCompressionType != 0)
        {
            DecryptBlockInPlace(blocksInfoBytes);
            blocksInfoRaw = Lz4Decompress(blocksInfoBytes, header.UncompressedBlocksInfoSize);
        }
        else
        {
            blocksInfoRaw = blocksInfoBytes;
        }

        var blocks = new List<VfsBlockInfo>();
        var nodes = new List<VfsNodeInfo>();

        using (var blocksStream = new MemoryStream(blocksInfoRaw, false))
        using (var blocksReader = new BinaryReader(blocksStream))
        {
            blocks = ReadBlocksInfo(blocksReader);
            nodes = ReadNodesInfo(blocksReader);
        }

        var dataOffset = header.EncFlags >= 7 ? 48 : 40;
        if ((header.Flags & 0x80u) == 0)
        {
            var temp = header.CompressedBlocksInfoSize;
            if ((header.Flags & 0x200u) != 0)
                temp = (temp + 15) & ~15;

            dataOffset += temp;
        }

        using var dataReader = new BinaryReader(new MemoryStream(input, false));
        dataReader.BaseStream.Position = dataOffset;

        using var blocksUncompressed = new MemoryStream();
        foreach (var block in blocks)
        {
            if (block.CompressionType != 5)
            {
                if (block.CompressionType == 0)
                {
                    var raw = dataReader.ReadBytes(block.UncompressedSize);
                    if (raw.Length != block.UncompressedSize)
                        throw new EndOfStreamException("Unexpected end of file while reading raw block data.");

                    blocksUncompressed.Write(raw, 0, raw.Length);
                    continue;
                }

                throw new InvalidDataException($"Unsupported Endfield block compression type: {block.CompressionType}");
            }

            var compressed = dataReader.ReadBytes((int)block.CompressedSize);
            if (compressed.Length != block.CompressedSize)
                throw new EndOfStreamException("Unexpected end of file while reading compressed block data.");

            DecryptBlockInPlace(compressed);
            var uncompressed = Lz4InvDecompress(compressed, block.UncompressedSize);
            blocksUncompressed.Write(uncompressed, 0, uncompressed.Length);
        }

        var blocksUncompressedBytes = blocksUncompressed.ToArray();
        var node = ChoosePrimaryNode(nodes, blocksUncompressedBytes.LongLength);
        if (node == null)
            throw new InvalidDataException("No node entry found in VFS directory info.");

        if (node.Offset < 0 || node.Size <= 0 || node.Offset + node.Size > blocksUncompressedBytes.LongLength ||
            node.Offset > int.MaxValue || node.Size > int.MaxValue)
            throw new InvalidDataException("Invalid node offset/size for decoded blocks stream.");

        var unityFs = new byte[(int)node.Size];
        Buffer.BlockCopy(blocksUncompressedBytes, (int)node.Offset, unityFs, 0, unityFs.Length);
        var read = unityFs.Length;
        if (read != unityFs.Length)
            throw new EndOfStreamException("Failed to read full UnityFS payload from decoded blocks stream.");

        var signature = ExtractAsciiPrefix(unityFs, 8);
        return new EndfieldVfsDecodeResult(
            Header: header,
            Blocks: blocks,
            Nodes: nodes,
            SelectedNode: node,
            BlocksUncompressedBytes: blocksUncompressedBytes,
            UnityFsBytes: unityFs,
            UnityFsSignature: signature);
    }

    private static void ValidateHeader(BinaryReader br)
    {
        var a = ReadUInt32BigEndian(br);
        var b = ReadUInt32BigEndian(br);

        var x = a ^ HeaderMask;
        var c1 = (4u * x) & 0xFFFF0000u;
        var c2 = RotateRight32(x, 14);
        var c3 = (c1 ^ c2 ^ HeaderCheckMask) & 0xFFFFFFFFu;
        if (b != c3)
            throw new InvalidDataException("Input is not a valid Endfield VFS-encrypted bundle header.");
    }

    private static EndfieldVfsHeader ReadHeader(BinaryReader br)
    {
        var cbs2 = ReadUInt16BigEndian(br);
        var flags2 = ReadUInt32BigEndian(br);
        var encFlags = ReadUInt32BigEndian(br);
        var size2 = ReadUInt32BigEndian(br);
        var flags1 = ReadUInt32BigEndian(br);
        var ubs1 = ReadUInt16BigEndian(br);
        ReadUInt32BigEndian(br); // unknown
        var ubs2 = ReadUInt16BigEndian(br);
        var size1 = ReadUInt32BigEndian(br);
        var cbs1 = ReadUInt16BigEndian(br);
        br.ReadByte(); // unknown

        var compressedBlocksInfoSize = BitConcat16((ushort)(cbs1 ^ cbs2 ^ 0xA121), cbs2);
        compressedBlocksInfoSize = RotateRight32(compressedBlocksInfoSize, 18) ^ HeaderFieldMask32;

        var uncompressedBlocksInfoSize = BitConcat16((ushort)(ubs1 ^ ubs2 ^ 0xA121), ubs2);
        uncompressedBlocksInfoSize = RotateRight32(uncompressedBlocksInfoSize, 18) ^ HeaderFieldMask32;

        var size = BitConcat32(size1 ^ size2 ^ 0xDAD76848u, size2);
        size = RotateRight64(size, 18) ^ HeaderFieldMask64;

        var decodedEncFlags = encFlags ^ flags2;
        var flags = flags1 ^ flags2 ^ 0xA7F49310u;

        return new EndfieldVfsHeader(
            Size: size,
            Flags: flags,
            EncFlags: decodedEncFlags,
            CompressedBlocksInfoSize: (int)compressedBlocksInfoSize,
            UncompressedBlocksInfoSize: (int)uncompressedBlocksInfoSize);
    }

    private static List<VfsBlockInfo> ReadBlocksInfo(BinaryReader br)
    {
        var encCountRaw = BinaryPrimitives.ReverseEndianness(ReadUInt32LittleEndian(br) ^ 0x8A7BF723u);
        var low = (ushort)encCountRaw;
        var high = (ushort)(encCountRaw >> 16);
        var blockCount = BitConcat16((ushort)(low ^ high), low);
        blockCount = RotateRight32(blockCount, 18) ^ BlocksCountMask;

        if (blockCount > int.MaxValue)
            throw new InvalidDataException($"Invalid blocks count: {blockCount}");

        var list = new List<VfsBlockInfo>((int)blockCount);
        for (var i = 0; i < blockCount; i++)
        {
            var a = ReadUInt16BigEndian(br);
            var b = ReadUInt16BigEndian(br);
            var c = ReadUInt16BigEndian(br);
            var encFlags = (ushort)(ReadUInt16BigEndian(br) ^ BlockFlagsXor);
            var d = ReadUInt16BigEndian(br);

            var a0 = (byte)encFlags;
            var a1 = (byte)(encFlags >> 8);
            var flags16 = BitConcat8((byte)(a0 ^ a1), a0);
            var flags = (ushort)(c ^ RotateLeft16(flags16, 14) ^ BlockFlagsMask);

            var uncompressedSize = BitConcat16((ushort)(a ^ c ^ 0xA121), c);
            uncompressedSize = RotateRight32(uncompressedSize, 18) ^ HeaderFieldMask32;

            var compressedSize = BitConcat16((ushort)(b ^ d ^ 0xA121), d);
            compressedSize = RotateRight32(compressedSize, 18) ^ HeaderFieldMask32;

            list.Add(new VfsBlockInfo((int)compressedSize, (int)uncompressedSize, flags));
        }

        return list;
    }

    private static List<VfsNodeInfo> ReadNodesInfo(BinaryReader br)
    {
        var encCountRaw = BinaryPrimitives.ReverseEndianness(ReadUInt32LittleEndian(br) ^ NodeCountXor);
        var low = (ushort)encCountRaw;
        var high = (ushort)(encCountRaw >> 16);
        var nodeCount = BitConcat16((ushort)(low ^ high), low);
        nodeCount = RotateRight32(nodeCount, 18) ^ NodesCountMask;

        if (nodeCount > int.MaxValue)
            throw new InvalidDataException($"Invalid nodes count: {nodeCount}");

        var list = new List<VfsNodeInfo>((int)nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            var a = ReadUInt32BigEndian(br) ^ 0x8E06A9F8u;
            var b = ReadUInt32BigEndian(br);
            var c = ReadUInt32BigEndian(br);
            var d = ReadUInt32BigEndian(br);

            var nameBytes = new List<byte>(64);
            while (br.BaseStream.Position < br.BaseStream.Length && nameBytes.Count < 64)
            {
                var bt = br.ReadByte();
                if (bt == 0)
                    break;

                nameBytes.Add(bt);
            }

            for (var j = 0; j < nameBytes.Count; j++)
                nameBytes[j] ^= (byte)((j ^ 0x97) & 0xFF);

            var name = Encoding.ASCII.GetString(nameBytes.ToArray());
            var e = ReadUInt32BigEndian(br);

            var a0 = (ushort)a;
            var a1 = (ushort)(a >> 16);
            var flags = BitConcat16((ushort)(a1 ^ a0), a0);
            flags = RotateRight32(flags, 18) ^ 0xF13927C4u ^ b;

            var offset = BitConcat32(d ^ c ^ 0xDAD76848u, c);
            offset = RotateLeft64(offset, 14) ^ HeaderFieldMask64;

            var size = BitConcat32(b ^ e ^ 0xDAD76848u, e);
            size = RotateLeft64(size, 14) ^ HeaderFieldMask64;

            list.Add(new VfsNodeInfo((long)offset, (long)size, flags, name));
        }

        return list;
    }

    private static void DecryptBlockInPlace(byte[] buffer)
    {
        var vfsaes = new VfsAes(VfsSBox, VfsAesKey, VfsAesIv, VfsAesXorKey);
        if (buffer.Length <= 256)
        {
            var decData = vfsaes.Decrypt(buffer);
            Buffer.BlockCopy(decData, 0, buffer, 0, buffer.Length);
            return;
        }

        var numBlocksFloor = buffer.Length / 16;
        var step = 256 / numBlocksFloor;
        if (numBlocksFloor > 256)
            step = 1;

        var decBuffer = new byte[256];
        for (var i = 0; i < Math.Min(numBlocksFloor, 256); i++)
            Buffer.BlockCopy(buffer, i * 16, decBuffer, i * step, step);

        var decData2 = vfsaes.Decrypt(decBuffer);
        for (var i = 0; i < Math.Min(numBlocksFloor, 256); i++)
            Buffer.BlockCopy(decData2, i * step, buffer, i * 16, step);
    }

    private static byte[] Lz4InvDecompress(ReadOnlySpan<byte> compressed, int expectedSize)
    {
        var output = new byte[expectedSize];
        var cmpPos = 0;
        var decPos = 0;

        while (cmpPos < compressed.Length && decPos < output.Length)
        {
            var token = compressed[cmpPos++];
            var litRaw = token & 0b0011_0011;
            var encRaw = token & 0b1100_1100;
            encRaw >>= 2;

            var litCount = (litRaw & 0b11) | (litRaw >> 2);
            var encCount = (encRaw & 0b11) | (encRaw >> 2);

            litCount = ReadLzLength(litCount, compressed, ref cmpPos);
            if (cmpPos + litCount > compressed.Length || decPos + litCount > output.Length)
                throw new InvalidDataException("Invalid LZ4Inv literal block range.");

            compressed.Slice(cmpPos, litCount).CopyTo(output.AsSpan(decPos, litCount));
            cmpPos += litCount;
            decPos += litCount;

            if (cmpPos >= compressed.Length)
                break;

            if (cmpPos + 1 >= compressed.Length)
                throw new InvalidDataException("Invalid LZ4Inv back-reference offset.");

            var back = (compressed[cmpPos++] << 8) | compressed[cmpPos++];
            encCount = ReadLzLength(encCount, compressed, ref cmpPos) + 4;

            var encPos = decPos - back;
            if (encPos < 0)
                throw new InvalidDataException("Invalid LZ4Inv back-reference position.");

            if (encCount <= back)
            {
                if (decPos + encCount > output.Length)
                    throw new InvalidDataException("Invalid LZ4Inv output range.");

                output.AsSpan(encPos, encCount).CopyTo(output.AsSpan(decPos, encCount));
                decPos += encCount;
            }
            else
            {
                while (encCount-- > 0)
                {
                    if (decPos >= output.Length || encPos < 0 || encPos >= output.Length)
                        throw new InvalidDataException("Invalid LZ4Inv overlapping copy range.");

                    output[decPos++] = output[encPos++];
                }
            }
        }

        if (decPos != expectedSize)
            throw new InvalidDataException($"LZ4Inv decompress size mismatch, expected={expectedSize}, actual={decPos}");

        return output;
    }

    private static byte[] Lz4Decompress(ReadOnlySpan<byte> compressed, int expectedSize)
    {
        var output = new byte[expectedSize];
        var cmpPos = 0;
        var decPos = 0;

        while (cmpPos < compressed.Length && decPos < output.Length)
        {
            var token = compressed[cmpPos++];
            var encCount = token & 0x0F;
            var litCount = (token >> 4) & 0x0F;

            litCount = ReadLzLength(litCount, compressed, ref cmpPos);
            if (cmpPos + litCount > compressed.Length || decPos + litCount > output.Length)
                throw new InvalidDataException("Invalid LZ4 literal block range.");

            compressed.Slice(cmpPos, litCount).CopyTo(output.AsSpan(decPos, litCount));
            cmpPos += litCount;
            decPos += litCount;

            if (cmpPos >= compressed.Length)
                break;

            if (cmpPos + 1 >= compressed.Length)
                throw new InvalidDataException("Invalid LZ4 back-reference offset.");

            var back = compressed[cmpPos++] | (compressed[cmpPos++] << 8);
            encCount = ReadLzLength(encCount, compressed, ref cmpPos) + 4;

            var encPos = decPos - back;
            if (encPos < 0)
                throw new InvalidDataException("Invalid LZ4 back-reference position.");

            if (encCount <= back)
            {
                if (decPos + encCount > output.Length)
                    throw new InvalidDataException("Invalid LZ4 output range.");

                output.AsSpan(encPos, encCount).CopyTo(output.AsSpan(decPos, encCount));
                decPos += encCount;
            }
            else
            {
                while (encCount-- > 0)
                {
                    if (decPos >= output.Length || encPos < 0 || encPos >= output.Length)
                        throw new InvalidDataException("Invalid LZ4 overlapping copy range.");

                    output[decPos++] = output[encPos++];
                }
            }
        }

        if (decPos != expectedSize)
            throw new InvalidDataException($"LZ4 decompress size mismatch, expected={expectedSize}, actual={decPos}");

        return output;
    }

    private static int ReadLzLength(int length, ReadOnlySpan<byte> compressed, ref int cmpPos)
    {
        if (length != 0xF)
            return length;

        byte sum;
        do
        {
            if (cmpPos >= compressed.Length)
                throw new EndOfStreamException("Unexpected end while reading LZ4Inv length.");

            sum = compressed[cmpPos++];
            length += sum;
        }
        while (sum == 0xFF);

        return length;
    }

    private static VfsNodeInfo? ChoosePrimaryNode(List<VfsNodeInfo> nodes, long blocksLen)
    {
        if (nodes.Count == 0)
            return null;

        foreach (var node in nodes)
        {
            if (node.Offset < 0 || node.Size <= 0)
                continue;

            if (node.Offset + node.Size > blocksLen)
                continue;

            return node;
        }

        return null;
    }

    private static string ExtractAsciiPrefix(byte[] bytes, int max)
    {
        var len = Math.Min(max, bytes.Length);
        var sb = new StringBuilder(len);
        for (var i = 0; i < len; i++)
        {
            var b = bytes[i];
            sb.Append(b is >= 32 and <= 126 ? (char)b : '.');
        }

        return sb.ToString();
    }

    private static uint ReadUInt32BigEndian(BinaryReader br)
    {
        Span<byte> buf = stackalloc byte[4];
        var read = br.Read(buf);
        if (read != 4)
            throw new EndOfStreamException();

        return BinaryPrimitives.ReadUInt32BigEndian(buf);
    }

    private static uint ReadUInt32LittleEndian(BinaryReader br)
    {
        Span<byte> buf = stackalloc byte[4];
        var read = br.Read(buf);
        if (read != 4)
            throw new EndOfStreamException();

        return BinaryPrimitives.ReadUInt32LittleEndian(buf);
    }

    private static ushort ReadUInt16BigEndian(BinaryReader br)
    {
        Span<byte> buf = stackalloc byte[2];
        var read = br.Read(buf);
        if (read != 2)
            throw new EndOfStreamException();

        return BinaryPrimitives.ReadUInt16BigEndian(buf);
    }

    private static ushort BitConcat8(byte a, byte b) => (ushort)(((ushort)a << 8) | b);
    private static uint BitConcat16(ushort a, ushort b) => ((uint)a << 16) | b;
    private static ulong BitConcat32(uint a, uint b) => ((ulong)a << 32) | b;

    private static ushort RotateLeft16(ushort value, int count) => (ushort)((value << count) | (value >> (16 - count)));
    private static uint RotateRight32(uint value, int count) => (value >> count) | (value << (32 - count));
    private static ulong RotateRight64(ulong value, int count) => (value >> count) | (value << (64 - count));
    private static ulong RotateLeft64(ulong value, int count) => (value << count) | (value >> (64 - count));

    private sealed class VfsAes
    {
        private readonly byte[] _sbox;
        private readonly byte[] _key;
        private readonly byte[] _iv;
        private readonly ulong _xorKey;

        public VfsAes(byte[] sbox, byte[] key, byte[] iv, ulong xorKey)
        {
            _sbox = sbox;
            _key = key;
            _iv = iv;
            _xorKey = xorKey;
        }

        public byte[] Decrypt(byte[] ciphertext)
        {
            var blocks = new List<byte[]>();
            var previous = (byte[])_iv.Clone();

            foreach (var ct in SplitBlocks(ciphertext))
            {
                var block = EncryptBlock(previous);
                var pt = new byte[ct.Length];
                for (var i = 0; i < ct.Length; i++)
                    pt[i] = (byte)(ct[i] ^ block[i]);

                blocks.Add(pt);

                var nextIv = new byte[16];
                var count = 0;
                for (var i = 0; i < 16; i++)
                {
                    var shiftSrc = _xorKey >> (count & 0x38);
                    var temp = (byte)(block[i] ^ (31 * i) ^ (byte)shiftSrc);
                    count += 8;
                    temp = (byte)(((temp >> 5) | (8 * temp)) & 0xFF);
                    nextIv[i] = _sbox[temp];
                }

                previous = nextIv;
            }

            var total = 0;
            foreach (var b in blocks)
                total += b.Length;

            var output = new byte[total];
            var offset = 0;
            foreach (var b in blocks)
            {
                Buffer.BlockCopy(b, 0, output, offset, b.Length);
                offset += b.Length;
            }

            return output;
        }

        private byte[] EncryptBlock(byte[] plaintext)
        {
            var keyMats = ExpandKey();
            const int nRounds = 10;
            var state = BytesToMatrix(plaintext);

            AddRoundKey(state, keyMats[0]);
            for (var r = 1; r < nRounds; r++)
            {
                SubBytes(state);
                ShiftRows(state);
                MixColumns(state);
                AddRoundKey(state, keyMats[r]);
            }

            SubBytes(state);
            ShiftRows(state);
            AddRoundKey(state, keyMats[^1]);
            return MatrixToBytes(state);
        }

        private IEnumerable<byte[]> SplitBlocks(byte[] msg)
        {
            for (var i = 0; i < msg.Length; i += 16)
            {
                var len = Math.Min(16, msg.Length - i);
                var outBlock = new byte[len];
                Buffer.BlockCopy(msg, i, outBlock, 0, len);
                yield return outBlock;
            }
        }

        private void SubBytes(List<byte[]> s)
        {
            for (var i = 0; i < 4; i++)
                for (var j = 0; j < 4; j++)
                    s[i][j] = _sbox[s[i][j]];
        }

        private static void ShiftRows(List<byte[]> s)
        {
            (s[0][1], s[1][1], s[2][1], s[3][1]) = (s[1][1], s[2][1], s[3][1], s[0][1]);
            (s[0][2], s[1][2], s[2][2], s[3][2]) = (s[2][2], s[3][2], s[0][2], s[1][2]);
            (s[0][3], s[1][3], s[2][3], s[3][3]) = (s[3][3], s[0][3], s[1][3], s[2][3]);
        }

        private static void AddRoundKey(List<byte[]> s, byte[,] k)
        {
            for (var i = 0; i < 4; i++)
                for (var j = 0; j < 4; j++)
                    s[i][j] ^= k[i, j];
        }

        private static byte XTime(byte a) => (byte)(((a & 0x80) != 0) ? ((a << 1) ^ 0x1B) & 0xFF : (a << 1));

        private static void MixSingleColumn(byte[] a)
        {
            var t = (byte)(a[0] ^ a[1] ^ a[2] ^ a[3]);
            var u = a[0];
            a[0] ^= (byte)(t ^ XTime((byte)(a[0] ^ a[1])));
            a[1] ^= (byte)(t ^ XTime((byte)(a[1] ^ a[2])));
            a[2] ^= (byte)(t ^ XTime((byte)(a[2] ^ a[3])));
            a[3] ^= (byte)(t ^ XTime((byte)(a[3] ^ u)));
        }

        private static void MixColumns(List<byte[]> s)
        {
            for (var i = 0; i < 4; i++)
                MixSingleColumn(s[i]);
        }

        private List<byte[,]> ExpandKey()
        {
            const int nRounds = 10;
            var keyCols = BytesToMatrix(_key);
            var iterationSize = _key.Length / 4;
            var i = 1;
            byte[] rCon = { 0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80, 0x1B, 0x36, 0x6C, 0xD8, 0xAB, 0x4D, 0x9A, 0x2F, 0x5E, 0xBC, 0x63, 0xC6, 0x97, 0x35, 0x6A, 0xD4, 0xB3, 0x7D, 0xFA, 0xEF, 0xC5, 0x91, 0x39 };

            while (keyCols.Count < (nRounds + 1) * 4)
            {
                var word = (byte[])keyCols[^1].Clone();
                if (keyCols.Count % iterationSize == 0)
                {
                    var first = word[0];
                    Array.Copy(word, 1, word, 0, word.Length - 1);
                    word[^1] = first;

                    for (var k = 0; k < 4; k++)
                        word[k] = _sbox[word[k]];

                    word[0] ^= rCon[i++];
                }

                var prev = keyCols[keyCols.Count - iterationSize];
                for (var k = 0; k < 4; k++)
                    word[k] ^= prev[k];

                keyCols.Add(word);
            }

            var result = new List<byte[,]>();
            for (var x = 0; x < keyCols.Count / 4; x++)
            {
                var m = new byte[4, 4];
                for (var c = 0; c < 4; c++)
                {
                    for (var r = 0; r < 4; r++)
                        m[c, r] = keyCols[x * 4 + c][r];
                }

                result.Add(m);
            }

            return result;
        }

        private static List<byte[]> BytesToMatrix(byte[] text)
        {
            var rows = new List<byte[]>(text.Length / 4);
            for (var i = 0; i < text.Length; i += 4)
                rows.Add(new[] { text[i], text[i + 1], text[i + 2], text[i + 3] });

            return rows;
        }

        private static byte[] MatrixToBytes(List<byte[]> m)
        {
            var r = new byte[16];
            var idx = 0;
            for (var i = 0; i < 4; i++)
                for (var j = 0; j < 4; j++)
                    r[idx++] = m[i][j];

            return r;
        }
    }
}

public sealed record EndfieldVfsDecodeResult(
    EndfieldVfsHeader Header,
    List<VfsBlockInfo> Blocks,
    List<VfsNodeInfo> Nodes,
    VfsNodeInfo SelectedNode,
    byte[] BlocksUncompressedBytes,
    byte[] UnityFsBytes,
    string UnityFsSignature);

public sealed record EndfieldVfsHeader(
    ulong Size,
    uint Flags,
    uint EncFlags,
    int CompressedBlocksInfoSize,
    int UncompressedBlocksInfoSize);

public sealed record VfsBlockInfo(int CompressedSize, int UncompressedSize, ushort Flags)
{
    public int CompressionType => Flags & 0x3F;
}

public sealed record VfsNodeInfo(long Offset, long Size, uint Flags, string Path);
