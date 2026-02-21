using System;
using System.Text;

namespace Endfield.BlcTool.Core.Crypto;

/// <summary>
/// Derives the common ChaCha20 key used by Endfield VFS metadata files (.blc).
/// The derivation matches the known game-side obfuscation strategy:
/// 1) concatenate selected key fragments,
/// 2) base64 decode,
/// 3) subtract master-key bytes modulo 256.
/// </summary>
public static class KeyDeriver
{
    // Fragmented key material observed in client data.
    private static readonly string[] KeyParts =
    {
        "Ks6k3zhrV5g",
        "uOVtMpqHxFv",
        "gi4BZzU9xUY",
        "CnBfZVqAgL",
        "SjpNhdKK89V",
        "qzl3BC/08Da",
        "oXafvEwR54",
        "4ZzYokf5I7Z"
    };

    // Master key used for byte-wise deobfuscation.
    private const string MasterKey = "Assets/Beyond/DynamicAssets/";

    /// <summary>
    /// Gets the 32-byte common ChaCha20 key.
    /// </summary>
    public static byte[] GetCommonChachaKey()
    {
        // Rebuild encoded key with the known fragment order.
        var encoded = KeyParts[0] + KeyParts[3] + KeyParts[5] + KeyParts[2] + "=";
        var base64Decoded = Convert.FromBase64String(encoded);
        var masterBytes = Encoding.UTF8.GetBytes(MasterKey);

        var result = new byte[32];
        // Reverse the simple additive obfuscation: (cipher - master + 256) % 256.
        for (var i = 0; i < result.Length && i < base64Decoded.Length; i++)
        {
            var cipherByte = base64Decoded[i];
            var masterByte = masterBytes[i % masterBytes.Length];
            result[i] = (byte)((cipherByte - masterByte + 256) % 256);
        }

        return result;
    }
}
