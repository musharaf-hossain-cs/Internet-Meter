namespace InternetMeter.Models;

public sealed record AppTrafficSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<AppTrafficRate> Apps,
    string Status);

public sealed record AppTrafficRate(
    string Process,
    int ProcessId,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond);
