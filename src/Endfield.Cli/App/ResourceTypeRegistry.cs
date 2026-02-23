using System;
using System.Collections.Generic;

namespace Endfield.Cli.App;

/// <summary>
/// Resource type name to group hash mapping used by chk-list/extract-type operations.
/// </summary>
public static class ResourceTypeRegistry
{
    private static readonly (string CanonicalName, string GroupHash)[] Items =
    {
        ("InitAudio", "07A1BB91"),
        ("InitBundle", "0CE8FA57"),
        ("InitialExtendData", "3C9D9D2D"),
        ("BundleManifest", "1CDDBF1F"),
        ("IFixPatchOut", "DAFE52C9"),

        ("AuditStreaming", "6432320A"),
        ("AuditDynamicStreaming", "B9358E30"),
        ("AuditIV", "06223FE2"),
        ("AuditAudio", "1EBAF5C6"),
        ("AuditVideo", "2E6CE44D"),

        ("Bundle", "7064D8E2"),
        ("Audio", "24ED34CF"),
        ("Video", "55FC21C6"),
        ("IV", "A63D7E6A"),
        ("Streaming", "C3442D43"),
        ("DynamicStreaming", "23D53F5D"),
        ("Lua", "19E3AE45"),
        ("Table", "42A8FCA6"),
        ("JsonData", "775A31D1"),
        ("ExtendData", "D6E622F7"),

        ("AudioChinese", "E1E7D7CE"),
        ("AudioEnglish", "A31457D0"),
        ("AudioJapanese", "F668D4EE"),
        ("AudioKorean", "E9D31017")
    };

    public static IEnumerable<string> GetSupportedTypeNames()
    {
        foreach (var item in Items)
            yield return item.CanonicalName;
    }

    public static bool TryGetGroupHashByTypeName(string resourceTypeName, out string groupHash, out string canonicalName)
    {
        if (string.IsNullOrWhiteSpace(resourceTypeName))
        {
            groupHash = string.Empty;
            canonicalName = string.Empty;
            return false;
        }

        var key = resourceTypeName.Trim();
        foreach (var item in Items)
        {
            if (!item.CanonicalName.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            canonicalName = item.CanonicalName;
            groupHash = item.GroupHash;
            return true;
        }

        groupHash = string.Empty;
        canonicalName = string.Empty;
        return false;
    }
}
