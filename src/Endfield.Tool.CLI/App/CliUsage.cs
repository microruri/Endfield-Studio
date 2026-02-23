using System;

namespace Endfield.Tool.CLI.App;

/// <summary>
/// Centralized help/usage output.
/// </summary>
public static class CliUsage
{
    /// <summary>
    /// Prints command usage, options, and examples.
    /// </summary>
    public static void Print()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Endfield.Tool.CLI -g <gameRoot> -t extract -n <resourceTypeName> -o <outputPath>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -g, --game-root   Game root directory.");
        Console.WriteLine("  -t, --type        Operation type. Supported: extract");
        Console.WriteLine("  -n, --name        Resource type name. Supported: InitAudio, InitBundle, InitialExtendData, BundleManifest, IFixPatchOut");
        Console.WriteLine("  -o, --output      Output directory for extracted files.");
        Console.WriteLine("  -d, --decode-content  Decode/decrypt extracted file content when decoder is available for selected type.");
        Console.WriteLine("  -h, --help        Show help.");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  Endfield.Tool.CLI -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t extract -n InitBundle -o \"out\\initbundle\"");
        Console.WriteLine("  Endfield.Tool.CLI -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t extract -n BundleManifest -o \"out\\manifest\" -d");
    }
}
