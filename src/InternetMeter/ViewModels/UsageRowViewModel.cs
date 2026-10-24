using InternetMeter.Models;
using InternetMeter.Services;

namespace InternetMeter.ViewModels;

public sealed class UsageRowViewModel(UsageRecord record)
{
    public DateOnly Day => record.Day;
    public string Download => SpeedFormatter.FormatBytes(record.DownloadBytes);
    public string Upload => SpeedFormatter.FormatBytes(record.UploadBytes);
    public string Total => SpeedFormatter.FormatBytes(record.TotalBytes);
    public UsageRecord Record => record;
}

