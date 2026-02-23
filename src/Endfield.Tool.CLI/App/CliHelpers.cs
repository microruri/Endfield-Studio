using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Endfield.Tool.CLI.App;

/// <summary>
/// Shared helpers for path and output sanitization.
/// </summary>
public static class CliHelpers
{
    /// <summary>
    /// Picks preferred path first, fallback path second.
    /// </summary>
    public static string? ResolvePreferredPath(string preferredPath, string fallbackPath)
    {
        if (File.Exists(preferredPath))
            return preferredPath;

        if (File.Exists(fallbackPath))
            return fallbackPath;

        return null;
    }

    /// <summary>
    /// Converts a virtual file path into a safe relative local path.
    /// </summary>
    public static string BuildSafeRelativePath(string virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            return string.Empty;

        var normalized = virtualPath.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var safeParts = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part == "." || part == "..")
                continue;

            var sb = new StringBuilder(part.Length);
            foreach (var ch in part)
                sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);

            var value = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(value))
                safeParts.Add(value);
        }

        if (safeParts.Count == 0)
            return string.Empty;

        return Path.Combine(safeParts.ToArray());
    }
}
