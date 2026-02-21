using System;
using Endfield.BlcTool.Core.Crypto;

namespace Endfield.BlcTool.Core.Blc;

/// <summary>
/// Decrypts Endfield .blc metadata blobs.
/// Layout: [12-byte nonce][ChaCha20-encrypted payload].
/// </summary>
public static class BlcDecryptor
{
    // First 12 bytes are reused as ChaCha20 nonce.
    private const int HeaderLength = 12;

    /// <summary>
    /// Returns merged bytes where the first 12 bytes remain untouched
    /// and the remaining part is decrypted payload.
    /// </summary>
    public static byte[] Decrypt(byte[] encryptedData)
    {
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));
        if (encryptedData.Length <= HeaderLength)
            throw new ArgumentException("Invalid .blc data length.", nameof(encryptedData));

        var key = KeyDeriver.GetCommonChachaKey();

        // nonce is stored directly in the .blc leading bytes.
        var nonce = new byte[HeaderLength];
        Buffer.BlockCopy(encryptedData, 0, nonce, 0, HeaderLength);

        // Everything after the nonce is cipher text.
        var cipher = new byte[encryptedData.Length - HeaderLength];
        Buffer.BlockCopy(encryptedData, HeaderLength, cipher, 0, cipher.Length);

        var plain = ChaCha20Cipher.Decrypt(key, nonce, 1, cipher);

        // Parser expects the same memory layout as source data,
        // so keep nonce/header and append decrypted payload.
        var merged = new byte[encryptedData.Length];
        Buffer.BlockCopy(encryptedData, 0, merged, 0, HeaderLength);
        Buffer.BlockCopy(plain, 0, merged, HeaderLength, plain.Length);
        return merged;
    }
}
