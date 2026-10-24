namespace InternetMeter.Models;

public sealed record NetworkSample(
    DateTimeOffset Timestamp,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    long DownloadBytesDelta,
    long UploadBytesDelta,
    IReadOnlyList<AdapterReading> Adapters)
{
    public static readonly NetworkSample Empty = new(
        DateTimeOffset.Now, 0, 0, 0, 0, Array.Empty<AdapterReading>());
}

public sealed record AdapterReading(
    string Id,
    string Name,
    string Description,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    long ReceivedBytes,
    long SentBytes);

