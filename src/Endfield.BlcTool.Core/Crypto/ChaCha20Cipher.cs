using System;
using System.Buffers.Binary;

namespace Endfield.BlcTool.Core.Crypto;

/// <summary>
/// Minimal ChaCha20 implementation used for Endfield metadata/file decryption.
/// This implementation follows RFC7539 layout (32-byte key, 12-byte nonce, 32-bit counter).
/// </summary>
public static class ChaCha20Cipher
{
    // "expand 32-byte k" constants as little-endian uints.
    private static readonly uint[] Sigma =
    {
        0x61707865, 0x3320646E, 0x79622D32, 0x6B206574
    };

    /// <summary>
    /// Decrypts bytes with ChaCha20.
    /// ChaCha20 is symmetric, so this is equivalent to encryption with the same key/nonce/counter.
    /// </summary>
    public static byte[] Decrypt(byte[] key, byte[] nonce, uint counter, byte[] input)
    {
        if (key.Length != 32)
            throw new ArgumentException("ChaCha20 key must be 32 bytes.", nameof(key));
        if (nonce.Length != 12)
            throw new ArgumentException("ChaCha20 nonce must be 12 bytes.", nameof(nonce));

        var output = new byte[input.Length];
        Process(key, nonce, counter, input, output);
        return output;
    }

    private static void Process(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, uint counter, ReadOnlySpan<byte> input, Span<byte> output)
    {
        // State and one 64-byte keystream block buffer.
        Span<uint> state = stackalloc uint[16];
        Span<byte> keystream = stackalloc byte[64];

        InitState(state, key, nonce, counter);

        var offset = 0;
        // Process input in 64-byte blocks; increment block counter after each block.
        while (offset < input.Length)
        {
            GenerateBlock(state, keystream);
            var len = Math.Min(64, input.Length - offset);
            for (var i = 0; i < len; i++)
                output[offset + i] = (byte)(input[offset + i] ^ keystream[i]);

            offset += len;
            state[12]++;
        }
    }

    private static void InitState(Span<uint> state, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, uint counter)
    {
        // 0..3: constants, 4..11: key, 12: counter, 13..15: nonce.
        state[0] = Sigma[0];
        state[1] = Sigma[1];
        state[2] = Sigma[2];
        state[3] = Sigma[3];

        for (var i = 0; i < 8; i++)
            state[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(i * 4, 4));

        state[12] = counter;
        state[13] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.Slice(0, 4));
        state[14] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.Slice(4, 4));
        state[15] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.Slice(8, 4));
    }

    private static void GenerateBlock(ReadOnlySpan<uint> state, Span<byte> output)
    {
        // Working state is mutated by 20 rounds, then added to original state.
        Span<uint> working = stackalloc uint[16];
        state.CopyTo(working);

        for (var i = 0; i < 10; i++)
        {
            // Odd round (column rounds).
            QuarterRound(ref working[0], ref working[4], ref working[8], ref working[12]);
            QuarterRound(ref working[1], ref working[5], ref working[9], ref working[13]);
            QuarterRound(ref working[2], ref working[6], ref working[10], ref working[14]);
            QuarterRound(ref working[3], ref working[7], ref working[11], ref working[15]);

            // Even round (diagonal rounds).
            QuarterRound(ref working[0], ref working[5], ref working[10], ref working[15]);
            QuarterRound(ref working[1], ref working[6], ref working[11], ref working[12]);
            QuarterRound(ref working[2], ref working[7], ref working[8], ref working[13]);
            QuarterRound(ref working[3], ref working[4], ref working[9], ref working[14]);
        }

        for (var i = 0; i < 16; i++)
        {
            var v = unchecked(working[i] + state[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(i * 4, 4), v);
        }
    }

    private static void QuarterRound(ref uint a, ref uint b, ref uint c, ref uint d)
    {
        a += b; d ^= a; d = Rotl(d, 16);
        c += d; b ^= c; b = Rotl(b, 12);
        a += b; d ^= a; d = Rotl(d, 8);
        c += d; b ^= c; b = Rotl(b, 7);
    }

    private static uint Rotl(uint x, int n) => (x << n) | (x >> (32 - n));
}
