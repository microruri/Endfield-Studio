using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Endfield.Cli.App;

/// <summary>
/// Shared helpers for path and output sanitization.
/// </summary>
public static class CliHelpers
{
    public static string? ResolvePreferredPath(string preferredPath, string fallbackPath)
    {
        if (File.Exists(preferredPath))
            return preferredPath;

        if (File.Exists(fallbackPath))
            return fallbackPath;

        return null;
    }

    public static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input.Trim())
            sb.Append(Array.IndexOf(invalidChars, ch) >= 0 ? '_' : ch);

        return sb.ToString();
    }

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
