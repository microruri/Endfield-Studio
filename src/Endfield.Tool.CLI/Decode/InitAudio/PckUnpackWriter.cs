using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Endfield.Tool.CLI.Decode.InitAudio;

/// <summary>
/// Writes simplified unpack outputs for one decrypted PCK payload.
/// </summary>
public static class PckUnpackWriter
{
    /// <summary>
    /// Unpacks one PCK into same-name directory and returns summary.
    /// </summary>
    public static bool TryUnpackToDirectory(string outputPckPath, string virtualFileName, byte[] sourceData, out string message)
    {
        message = string.Empty;

        try
        {
            var parse = PckParser.Parse(sourceData, out var decipheredHeaderData);

            // Replace target output file with folder-style package dump.
            var rootPath = outputPckPath;
            var rootDir = Path.GetDirectoryName(rootPath);
            if (!string.IsNullOrEmpty(rootDir))
                Directory.CreateDirectory(rootDir);

            if (File.Exists(rootPath))
                File.Delete(rootPath);

            if (Directory.Exists(rootPath))
                Directory.Delete(rootPath, recursive: true);

            Directory.CreateDirectory(rootPath);

            var records = new List<PckEntryExportRecord>(parse.Entries.Count);
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var successCount = 0;
            var failCount = 0;

            foreach (var entry in parse.Entries)
            {
                var record = new PckEntryExportRecord
                {
                    GlobalIndex = entry.GlobalIndex,
                    Entry = entry
                };

                if (!entry.OffsetSizeInRange)
                {
                    record.Error = "entry offset/size out of file range";
                    failCount++;
                    records.Add(record);
                    continue;
                }

                var absoluteStart = (int)entry.Offset;
                var encPayload = Slice(decipheredHeaderData, absoluteStart, (int)entry.Size);

                var decPayload = new byte[encPayload.Length];
                Buffer.BlockCopy(encPayload, 0, decPayload, 0, encPayload.Length);
                PckCipher.DecipherInPlace(decPayload, 0, unchecked((uint)entry.Id), decPayload.Length);
                record.DecPayloadLength = decPayload.Length;

                var kind = DetectKind(decPayload);
                record.DecPayloadKind = kind;
                var outputName = BuildOutputFileName(entry, kind, usedFileNames);
                var outputPath = Path.Combine(rootPath, outputName);
                File.WriteAllBytes(outputPath, decPayload);
                record.OutputFile = outputName;

                if (kind == PckPayloadKind.Unknown)
                {
                    // Preserve unknown payloads as .bin while still fully exporting content.
                }

                successCount++;
                records.Add(record);
            }

            var manifest = new PckUnpackManifest
            {
                SourceVirtualPath = virtualFileName,
                SourceLength = sourceData.Length,
                Parse = parse,
                Entries = records
            };

            var manifestJson = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllBytes(Path.Combine(rootPath, "_manifest.pck.json"), manifestJson);

            message = $"pck unpacked: entries={parse.Entries.Count}, success={successCount}, failed={failCount}, out={ToDisplayPath(rootPath)}";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private static string BuildOutputFileName(PckEntryInfo entry, PckPayloadKind kind, HashSet<string> usedFileNames)
    {
        var ext = kind switch
        {
            PckPayloadKind.Wem => ".wem",
            PckPayloadKind.Bnk => ".bnk",
            PckPayloadKind.Plug => ".plug",
            _ => ".bin"
        };

        // wwiser/vgmstream interoperability expects SourceID-decimal .wem names.
        // Use low-32-bit decimal for WEM/BNK; keep stable index+hex naming for unknown/blob payloads.
        var baseName = kind is PckPayloadKind.Wem or PckPayloadKind.Bnk
            ? entry.IdLow.ToString(CultureInfo.InvariantCulture)
            : $"{entry.GlobalIndex:D6}_{entry.Id:X16}";

        var candidate = baseName + ext;
        if (usedFileNames.Add(candidate))
            return candidate;

        // Avoid accidental overwrite on rare ID collisions.
        var withIndex = $"{baseName}_{entry.GlobalIndex:D6}{ext}";
        if (usedFileNames.Add(withIndex))
            return withIndex;

        var salt = 1;
        while (true)
        {
            var fallback = $"{baseName}_{entry.GlobalIndex:D6}_{salt:D2}{ext}";
            if (usedFileNames.Add(fallback))
                return fallback;

            salt++;
        }
    }

    private static byte[] Slice(byte[] source, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > source.Length)
            return Array.Empty<byte>();

        var output = new byte[length];
        Buffer.BlockCopy(source, offset, output, 0, length);
        return output;
    }

    private static PckPayloadKind DetectKind(byte[] payload)
    {
        if (payload.Length >= 4)
        {
            if (payload[0] == (byte)'R' && payload[1] == (byte)'I' && payload[2] == (byte)'F' && payload[3] == (byte)'F')
                return PckPayloadKind.Wem;
            if (payload[0] == (byte)'B' && payload[1] == (byte)'K' && payload[2] == (byte)'H' && payload[3] == (byte)'D')
                return PckPayloadKind.Bnk;
            if (payload[0] == (byte)'P' && payload[1] == (byte)'L' && payload[2] == (byte)'U' && payload[3] == (byte)'G')
                return PckPayloadKind.Plug;
        }

        return PckPayloadKind.Unknown;
    }

    private static string ToDisplayPath(string path)
    {
        return path.Replace('\\', '/');
    }
}
