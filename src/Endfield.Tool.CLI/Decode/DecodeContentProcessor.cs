using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Endfield.Tool.CLI.Decode.BundleManifest;
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

                    var token = new SparkBytesDecoder(input).Load();
                    var json = token.ToString(Newtonsoft.Json.Formatting.Indented);
                    output = Encoding.UTF8.GetBytes(json);
                    return true;

                default:
                    message = "no decoder configured for resource type";
                    return false;
            }
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }
}
