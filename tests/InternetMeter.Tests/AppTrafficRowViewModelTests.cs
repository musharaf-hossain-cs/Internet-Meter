using InternetMeter.Models;
using InternetMeter.ViewModels;

namespace InternetMeter.Tests;

public sealed class AppTrafficRowViewModelTests
{
    [Fact]
    public void FormatsDownloadUploadAndCombinedRate()
    {
        var row = new AppTrafficRowViewModel(
            new AppTrafficRate("browser", 42, 1_000, 500),
            displayBits: false);

        Assert.Equal("browser", row.Process);
        Assert.Equal(42, row.ProcessId);
        Assert.Equal("1.0 KB/s", row.Download);
        Assert.Equal("500 B/s", row.Upload);
        Assert.Equal("1.5 KB/s", row.Total);
    }
}
