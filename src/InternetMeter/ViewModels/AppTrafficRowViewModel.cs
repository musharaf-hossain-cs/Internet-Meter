using InternetMeter.Models;
using InternetMeter.Services;

namespace InternetMeter.ViewModels;

public sealed class AppTrafficRowViewModel(AppTrafficRate rate, bool displayBits)
{
    public string Process => rate.Process;
    public int ProcessId => rate.ProcessId;
    public double TotalBytesPerSecond => rate.DownloadBytesPerSecond + rate.UploadBytesPerSecond;
    public string Download => SpeedFormatter.Format(rate.DownloadBytesPerSecond, displayBits);
    public string Upload => SpeedFormatter.Format(rate.UploadBytesPerSecond, displayBits);
    public string Total => SpeedFormatter.Format(TotalBytesPerSecond, displayBits);
}
