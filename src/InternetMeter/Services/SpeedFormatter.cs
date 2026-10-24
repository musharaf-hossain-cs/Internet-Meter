using System.Globalization;

namespace InternetMeter.Services;

public static class SpeedFormatter
{
    private static readonly string[] ByteUnits = ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"];
    private static readonly string[] BitUnits = ["b/s", "Kb/s", "Mb/s", "Gb/s", "Tb/s"];

    public static string Format(double bytesPerSecond, bool bits = false, int decimals = 1)
    {
        if (double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond) || bytesPerSecond < 0)
            bytesPerSecond = 0;

        var value = bits ? bytesPerSecond * 8d : bytesPerSecond;
        var units = bits ? BitUnits : ByteUnits;
        var index = 0;
        while (value >= 1000d && index < units.Length - 1)
        {
            value /= 1000d;
            index++;
        }

        var places = value >= 100 ? 0 : value >= 10 ? Math.Min(decimals, 1) : decimals;
        return $"{value.ToString($"F{places}", CultureInfo.InvariantCulture)} {units[index]}";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1000d && index < units.Length - 1)
        {
            value /= 1000d;
            index++;
        }

        return $"{value:F2} {units[index]}";
    }
}

