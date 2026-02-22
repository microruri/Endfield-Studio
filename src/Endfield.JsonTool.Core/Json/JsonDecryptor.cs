using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Endfield.JsonTool.Core.Json;

/// <summary>
/// Decrypts Endfield index json blobs stored as base64 + byte-wise obfuscation.
/// </summary>
public static class JsonDecryptor
{
    private const string LegacyMasterKey = "Assets/Beyond/DynamicAssets/";
    private const string IndexMasterKey = "Assets/Beyond/DynamicAssets/Gameplay/UI/Fonts/";

    private static readonly string[] MasterKeys =
    {
        IndexMasterKey,
        LegacyMasterKey
    };

    /// <summary>
    /// Decrypts encrypted index json bytes into first-stage plain bytes.
    /// </summary>
    public static byte[] DecryptFirstStage(byte[] encryptedJsonData)
    {
        if (encryptedJsonData == null)
            throw new ArgumentNullException(nameof(encryptedJsonData));

        var base64Text = Encoding.ASCII.GetString(encryptedJsonData).Trim();
        if (string.IsNullOrWhiteSpace(base64Text))
            throw new ArgumentException("Input json data is empty.", nameof(encryptedJsonData));

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(base64Text);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Input is not valid base64 json content.", nameof(encryptedJsonData), ex);
        }

        foreach (var masterKey in MasterKeys)
        {
            var plain = Deobfuscate(decoded, masterKey);
            if (LooksLikeJson(plain))
                return plain;
        }

        return Deobfuscate(decoded, IndexMasterKey);
    }

    /// <summary>
    /// Attempts to decode first-stage bytes as strict UTF-8 JSON text.
    /// </summary>
    public static bool TryDecodeUtf8Json(byte[] firstStageBytes, out string jsonText)
    {
        jsonText = string.Empty;
        if (firstStageBytes == null)
            return false;

        var utf8 = new UTF8Encoding(false, true);
        try
        {
            jsonText = utf8.GetString(firstStageBytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts readable text fragments from first-stage bytes for quick analysis.
    /// </summary>
    public static string BuildPrintablePreview(byte[] firstStageBytes)
    {
        if (firstStageBytes == null)
            throw new ArgumentNullException(nameof(firstStageBytes));

        var parts = new List<string>();
        var sb = new StringBuilder();
        foreach (var b in firstStageBytes)
        {
            if (b >= 32 && b <= 126)
            {
                sb.Append((char)b);
                continue;
            }

            if (sb.Length >= 8)
                parts.Add(sb.ToString());

            sb.Clear();
        }

        if (sb.Length >= 8)
            parts.Add(sb.ToString());

        return string.Join(Environment.NewLine, parts.Take(300));
    }

    private static byte[] Deobfuscate(byte[] decoded, string masterKey)
    {
        var key = Encoding.UTF8.GetBytes(masterKey);
        var plain = new byte[decoded.Length];
        for (var i = 0; i < decoded.Length; i++)
            plain[i] = (byte)((decoded[i] - key[i % key.Length] + 256) % 256);

        return plain;
    }

    private static bool LooksLikeJson(byte[] bytes)
    {
        if (bytes.Length == 0)
            return false;

        var i = 0;
        while (i < bytes.Length && (bytes[i] == (byte)' ' || bytes[i] == (byte)'\t' || bytes[i] == (byte)'\r' || bytes[i] == (byte)'\n'))
            i++;

        if (i >= bytes.Length)
            return false;

        var first = bytes[i];
        return first == (byte)'{' || first == (byte)'[';
    }
}
