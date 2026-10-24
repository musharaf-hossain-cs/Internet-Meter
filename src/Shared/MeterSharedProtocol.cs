namespace InternetMeter.Shared
{
    internal static class MeterSharedProtocol
    {
        internal const string MapName = @"Local\InternetMeter.TaskbarData.v1";
        internal const string MutexName = @"Local\InternetMeter.TaskbarDataLock.v1";
        internal const string ShowDashboardEventName = @"Local\InternetMeter.ShowDashboard.v1";
        internal const int Capacity = 512;
        internal const int Magic = 0x494D5452;
        internal const int Version = 1;

        internal const int MagicOffset = 0;
        internal const int VersionOffset = 4;
        internal const int TimestampOffset = 8;
        internal const int DownloadLengthOffset = 16;
        internal const int DownloadTextOffset = 20;
        internal const int TextCapacity = 96;
        internal const int UploadLengthOffset = DownloadTextOffset + TextCapacity;
        internal const int UploadTextOffset = UploadLengthOffset + 4;
        internal const int CompactOffset = UploadTextOffset + TextCapacity;
        internal const int DownloadColorOffset = CompactOffset + 4;
        internal const int UploadColorOffset = DownloadColorOffset + 4;
        internal const int BackgroundColorOffset = UploadColorOffset + 4;
    }
}
