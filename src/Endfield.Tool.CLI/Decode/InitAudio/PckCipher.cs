using System;

namespace Endfield.Tool.CLI.Decode.InitAudio;

/// <summary>
/// Endfield PCK XOR decipher helper.
/// </summary>
public static class PckCipher
{
    public const uint EncMagic = 0x4478293A;   // :)xD
    public const uint PlainMagic = 0x4B504B41; // AKPK

    private const uint Multiplier = 0x04E11C23;
    private const uint XorConstant = 0x9C5A0B29;

    /// <summary>
    /// Deciphers in place using the game's PCK algorithm.
    /// </summary>
    public static void DecipherInPlace(byte[] data, int offset, uint seed, int length)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (offset < 0 || length < 0 || offset + length > data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var dwordCount = length / 4;
        var currentSeed = seed;

        for (var i = 0; i < dwordCount; i++)
        {
            var key = GenerateXorKey(currentSeed++);
            var index = offset + i * 4;
            var value = BitConverter.ToUInt32(data, index) ^ key;
            var bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, data, index, 4);
        }

        var remain = length % 4;
        if (remain <= 0)
            return;

        var tailKey = BitConverter.GetBytes(GenerateXorKey(currentSeed));
        var tailOffset = offset + dwordCount * 4;
        for (var j = 0; j < remain; j++)
            data[tailOffset + j] ^= tailKey[j];
    }

    /// <summary>
    /// Determines whether magic indicates encrypted PCK.
    /// </summary>
    public static bool IsEncryptedMagic(uint magic)
    {
        return magic == EncMagic;
    }

    private static uint GenerateXorKey(uint seed)
    {
        var v1 = (seed & 0xFF) ^ XorConstant;
        var v2 = unchecked(Multiplier * v1);
        var v3 = v2 ^ ((seed >> 8) & 0xFF);
        var v4 = unchecked(Multiplier * v3);
        var v5 = v4 ^ ((seed >> 16) & 0xFF);
        var v6 = unchecked(Multiplier * v5);
        var v7 = v6 ^ (seed >> 24);
        return unchecked(Multiplier * v7);
    }
}
