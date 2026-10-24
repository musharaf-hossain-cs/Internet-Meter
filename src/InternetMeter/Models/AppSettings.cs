namespace InternetMeter.Models;

public sealed class AppSettings
{
    public int RefreshIntervalMilliseconds { get; set; } = 1000;
    public double SmoothingFactor { get; set; } = 0.35;
    public bool DisplayBits { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool StartWithWindows { get; set; }
    public bool IncludeVirtualAdapters { get; set; }
    public bool CompactSingleLine { get; set; }
    public string DownloadColor { get; set; } = "#38BDF8";
    public string UploadColor { get; set; } = "#A78BFA";
    public string MeterBackground { get; set; } = "#E6202020";
    public long MonthlyQuotaBytes { get; set; }
    public int QuotaWarningPercent { get; set; } = 80;
    public bool QuotaNotificationsEnabled { get; set; } = true;
    public double SpeedAlertMegabitsPerSecond { get; set; }
    public int SpeedAlertDurationSeconds { get; set; } = 10;
    public int HistoryRetentionDays { get; set; } = 730;
    public List<string> SelectedAdapterIds { get; set; } = [];
}

