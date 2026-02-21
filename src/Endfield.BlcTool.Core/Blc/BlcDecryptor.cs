using System;
using Endfield.BlcTool.Core.Crypto;

namespace Endfield.BlcTool.Core.Blc;

public static class BlcDecryptor
{
    private const int HeaderLength = 12;

    public static byte[] Decrypt(byte[] encryptedData)
    {
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));
        if (encryptedData.Length <= HeaderLength)
            throw new ArgumentException("Invalid .blc data length.", nameof(encryptedData));

        var key = KeyDeriver.GetCommonChachaKey();

        var nonce = new byte[HeaderLength];
        Buffer.BlockCopy(encryptedData, 0, nonce, 0, HeaderLength);

        var cipher = new byte[encryptedData.Length - HeaderLength];
        Buffer.BlockCopy(encryptedData, HeaderLength, cipher, 0, cipher.Length);

        var plain = ChaCha20Cipher.Decrypt(key, nonce, 1, cipher);

        var merged = new byte[encryptedData.Length];
        Buffer.BlockCopy(encryptedData, 0, merged, 0, HeaderLength);
        Buffer.BlockCopy(plain, 0, merged, HeaderLength, plain.Length);
        return merged;
    }
}
