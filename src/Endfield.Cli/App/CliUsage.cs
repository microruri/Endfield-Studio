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
        Console.WriteLine("  -t, --type        Operation type. Supported: blc-all, json-index, chk-list, extract-type");
        Console.WriteLine("  -o, --output      Output directory (required by blc-all/json-index/extract-type).");
        Console.WriteLine("  -n, --name        Resource type name (required by chk-list/extract-type).");
        Console.WriteLine("  -h, --help        Show help.");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t blc-all -o \"out\"");
        Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t chk-list -n AudioChinese");
        Console.WriteLine("  Endfield.Cli -g \"C:\\Program Files\\Hypergryph Launcher\\games\\EndField Game\" -t extract-type -n Lua -o \"out\\lua\"");
    }
}
