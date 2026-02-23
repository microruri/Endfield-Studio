using Endfield.Tool.CLI.App;

// Minimal entry point that delegates command handling to CliApplication.
var exitCode = CliApplication.Run(args);
Environment.Exit(exitCode);
