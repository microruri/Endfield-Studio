using System.Collections.Generic;

namespace Endfield.Cli.Extract;

/// <summary>
/// Parsed manifest model used for JSON output and follow-up analysis.
/// </summary>
public sealed class ManifestScheme
{
    public string Version { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string PerforceCL { get; set; } = string.Empty;
    public long AssetInfoAddress { get; set; }
    public long BundleAddress { get; set; }
    public long BundleArrayAddress { get; set; }
    public long DataAddress { get; set; }
    public List<ManifestBundle> Bundles { get; set; } = new();
    public List<ManifestAssetInfo> Assets { get; set; } = new();
}

public sealed class ManifestBundle
{
    public int BundleIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public int[] Dependencies { get; set; } = new int[0];
    public int[] DirectReverseDependencies { get; set; } = new int[0];
    public int[] DirectDependencies { get; set; } = new int[0];
    public int BundleFlags { get; set; }
    public long HashName { get; set; }
    public long HashVersion { get; set; }
    public byte Category { get; set; }
}

public sealed class ManifestAssetInfo
{
    public long PathHashHead { get; set; }
    public string Path { get; set; } = string.Empty;
    public int BundleIndex { get; set; }
    public int AssetSize { get; set; }
}
