namespace Endfield.Tool.GUI.Logging;

public sealed class GuiLogger
{
    public void Info(string message)
    {
        Log("INFO", message);
    }

    public void Warn(string message)
    {
        Log("WARN", message);
    }

    public void Error(string message)
    {
        Log("ERROR", message);
    }

    private void Log(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        Console.WriteLine(line);
    }
}
