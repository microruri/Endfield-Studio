using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Endfield.Tool.CLI.Decode.BundleManifest;
using Endfield.Tool.CLI.Decode.IFix;
using Endfield.Tool.CLI.Decode.SparkBuffer;
using Endfield.Tool.CLI.Decode.StringPath;

namespace Endfield.Tool.CLI.Decode;

/// <summary>
/// Dispatches content-level decode/decrypt for extracted files.
/// </summary>
public static class DecodeContentProcessor
{
    public static bool IsDecodeSupportedForType(string resourceTypeName)
    {
        return resourceTypeName switch
        {
            "InitialExtendData" => true,
            "BundleManifest" => true,
            "IFixPatchOut" => true,
            _ => false
        };
    }

    public static bool TryProcess(string resourceTypeName, string virtualFileName, byte[] input, out byte[] output, out string message)
    {
        output = input;
        message = string.Empty;

        try
        {
            switch (resourceTypeName)
            {
                case "InitialExtendData":
                    if (!virtualFileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                    {
                        message = "unsupported extension for InitialExtendData";
                        return false;
                    }

                    output = StringPathHashDecoder.DecodeToJsonBytes(input);
                    return true;

                case "BundleManifest":
                    if (!virtualFileName.EndsWith(".hgmmap", StringComparison.OrdinalIgnoreCase))
                    {
                        message = "unsupported extension for BundleManifest";
                        return false;
                    }

                    var manifest = ManifestDecoder.Decode(input);
                    output = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    return true;

                case "IFixPatchOut":
                    if (!virtualFileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                    {
                        message = "unsupported extension for IFixPatchOut";
                        return false;
                    }

                    if (!LooksLikeSparkHeader(input))
                    {
                        if (ILFixPatchReadableDecoder.LooksLikeIlFixPatch(input))
                        {
                            output = ILFixPatchReadableDecoder.DecodeToReadableTextBytes(input);
                            message = "decoded ILFix patch metadata view";
                            return true;
                        }

                        // Unknown non-Spark payload: keep raw bytes.
                        message = "non-Spark IFix patch binary detected; kept original payload";
                        return true;
                    }

                    try
                    {
                        var token = new SparkBytesDecoder(input).Load();
                        var json = token.ToString(Newtonsoft.Json.Formatting.Indented);
                        output = Encoding.UTF8.GetBytes(json);
                        message = "decoded SparkBuffer payload";
                        return true;
                    }
                    catch (Exception ex)
                    {
                        // Spark decode failed: try ILFix-readable projection.
                        if (ILFixPatchReadableDecoder.LooksLikeIlFixPatch(input))
                        {
                            output = ILFixPatchReadableDecoder.DecodeToReadableTextBytes(input);
                            message = "decoded ILFix patch metadata view";
                            return true;
                        }

                        // Keep extraction robust: unknown patch variants should still be exported.
                        message = $"Spark decode not applied ({ex.Message}); kept original payload";
                        return true;
                    }

                default:
                    message = "no decoder configured for resource type";
                    return false;
            }
        }
        catch (Exception ex)
        {
            if (resourceTypeName == "IFixPatchOut" && ILFixPatchReadableDecoder.LooksLikeIlFixPatch(input))
            {
                output = ILFixPatchReadableDecoder.DecodeToReadableTextBytes(input);
                message = $"fallback ILFix decode applied after exception: {ex.Message}";
                return true;
            }

            message = ex.Message;
            return false;
        }
    }

    private static bool LooksLikeSparkHeader(byte[] input)
    {
        if (input.Length < 12)
            return false;

        var typeDefsPtr = BitConverter.ToInt32(input, 0);
        var rootDefPtr = BitConverter.ToInt32(input, 4);
        var dataPtr = BitConverter.ToInt32(input, 8);

        if (typeDefsPtr < 0 || rootDefPtr < 0 || dataPtr < 0)
            return false;

        if (typeDefsPtr >= input.Length || rootDefPtr >= input.Length || dataPtr >= input.Length)
            return false;

        return true;
    }
}
