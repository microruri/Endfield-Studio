using System;
using System.Text;

namespace Endfield.BlcTool.Core.Crypto;

public static class KeyDeriver
{
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

    private const string MasterKey = "Assets/Beyond/DynamicAssets/";

    public static byte[] GetCommonChachaKey()
    {
        var encoded = KeyParts[0] + KeyParts[3] + KeyParts[5] + KeyParts[2] + "=";
        var base64Decoded = Convert.FromBase64String(encoded);
        var masterBytes = Encoding.UTF8.GetBytes(MasterKey);

        var result = new byte[32];
        for (var i = 0; i < result.Length && i < base64Decoded.Length; i++)
        {
            var cipherByte = base64Decoded[i];
            var masterByte = masterBytes[i % masterBytes.Length];
            result[i] = (byte)((cipherByte - masterByte + 256) % 256);
        }

        return result;
    }
}
