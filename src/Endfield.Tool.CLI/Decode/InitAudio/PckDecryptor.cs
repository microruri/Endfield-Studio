using System;
using System.Collections.Generic;

namespace Endfield.Tool.CLI.Decode.InitAudio;

/// <summary>
/// Decrypts Endfield PCK payload to a single decrypted .pck file image.
/// </summary>
public static class PckDecryptor
{
    /// <summary>
    /// Decrypts one PCK blob (header + entry payloads) into AKPK-compatible bytes.
    /// </summary>
    public static bool TryDecrypt(byte[] sourceData, out byte[] decryptedData, out string message)
    {
        decryptedData = Array.Empty<byte>();
        message = string.Empty;

        try
        {
            var parse = PckParser.Parse(sourceData, out var headerDeciphered);
            var output = new byte[headerDeciphered.Length];
            Buffer.BlockCopy(headerDeciphered, 0, output, 0, headerDeciphered.Length);

            var visitedRanges = new HashSet<string>(StringComparer.Ordinal);
            var decipheredRanges = 0;
            var skippedRanges = 0;

            foreach (var entry in parse.Entries)
            {
                if (!entry.OffsetSizeInRange || entry.Size > int.MaxValue)
                {
                    skippedRanges++;
                    continue;
                }

                var rangeKey = $"{entry.Offset}:{entry.Size}";
                if (!visitedRanges.Add(rangeKey))
                    continue;

                PckCipher.DecipherInPlace(output, (int)entry.Offset, unchecked((uint)entry.Id), (int)entry.Size);
                decipheredRanges++;
            }

            decryptedData = output;
            message = $"decrypted pck: entries={parse.Entries.Count}, decipheredRanges={decipheredRanges}, skipped={skippedRanges}";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }
}
