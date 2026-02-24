using System;
using System.Collections.Generic;

namespace Endfield.Tool.CLI.App;

/// <summary>
/// Resource types currently supported by Endfield.Tool.CLI extract operation.
/// </summary>
public static class ResourceTypeRegistry
{
    private static readonly ResourceTypeInfo[] Items =
    {
        new("InitAudio", 1),
        new("InitBundle", 2),
        new("InitialExtendData", 3),
        new("BundleManifest", 4),
        new("IFixPatchOut", 5),
        new("Audio", 12),
        new("AudioChinese", 101),
        new("AudioEnglish", 102),
        new("AudioJapanese", 103),
        new("AudioKorean", 104)
    };

    /// <summary>
    /// Returns all currently supported resource type names.
    /// </summary>
    public static IEnumerable<string> GetSupportedTypeNames()
    {
        foreach (var item in Items)
            yield return item.Name;
    }

    /// <summary>
    /// Tries to resolve one supported type by name.
    /// </summary>
    public static bool TryGetByName(string resourceTypeName, out ResourceTypeInfo info)
    {
        if (string.IsNullOrWhiteSpace(resourceTypeName))
        {
            info = default;
            return false;
        }

        var key = resourceTypeName.Trim();
        foreach (var item in Items)
        {
            if (!item.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            info = item;
            return true;
        }

        info = default;
        return false;
    }

    /// <summary>
    /// Resource type descriptor used by extract flow.
    /// </summary>
    public readonly record struct ResourceTypeInfo(string Name, int TypeId);
}
