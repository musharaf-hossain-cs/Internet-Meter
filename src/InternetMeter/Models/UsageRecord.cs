namespace InternetMeter.Models;

public sealed record UsageRecord(DateOnly Day, long DownloadBytes, long UploadBytes)
{
    public long TotalBytes => DownloadBytes + UploadBytes;
}

