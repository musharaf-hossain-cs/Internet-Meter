using System.IO;

namespace InternetMeter.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Internet Meter", "logs");

    public static void Error(Exception exception, string context)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"internet-meter-{DateTime.Today:yyyy-MM-dd}.log");
            lock (Gate)
            {
                File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {context}\r\n{exception}\r\n\r\n");
            }
            Cleanup();
        }
        catch { }
    }

    private static void Cleanup()
    {
        foreach (var file in Directory.EnumerateFiles(LogDirectory, "*.log"))
        {
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }
}

