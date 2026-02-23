using System;

namespace Endfield.Cli.App;

/// <summary>
/// Centralized help/usage output.
/// </summary>
public static class CliUsage
{
    public static void Print()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Endfield.Cli -g <gameRoot> -t <operation> [-o <outputPath>] [-n <resourceTypeName>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -g, --game-root   Game root directory.");
        Console.WriteLine("  -t, --type        Operation type. Supported: blc-all, json-index, chk-list, extract-type, manifest-assets-yaml");
        Console.WriteLine("  -o, --output      Output directory/file (required by blc-all/json-index/extract-type/manifest-assets-yaml).");
        Console.WriteLine("  -n, --name        Resource type name (required by chk-list/extract-type).");
        Console.WriteLine("  -d, --decode-content  Try content-level decode for extracted files (extract-type only).");
        Console.WriteLine("  -h, --help        Show help.");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t blc-all -o \"out\"");
        Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t chk-list -n AudioChinese");
        Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t extract-type -n Lua -o \"out\\lua\"");
        Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t extract-type -n BundleManifest -o \"out\\manifest\" -d");
        Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t manifest-assets-yaml -o \"out\\bundle_manifest_assets.yaml\"");
    }
}
