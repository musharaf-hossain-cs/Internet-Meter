using InternetMeter.Services;

namespace InternetMeter.Tests;

public sealed class UsageStoreTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InternetMeterTests", Guid.NewGuid().ToString("N"));
    private UsageStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new UsageStore(_directory);
        await _store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task PersistsAggregatedUsage()
    {
        _store.Record(1_200, 300);
        _store.Record(800, 700);
        await _store.FlushAsync();

        var rows = await _store.GetDailyUsageAsync(1);
        var today = Assert.Single(rows);
        Assert.Equal(2_000, today.DownloadBytes);
        Assert.Equal(1_000, today.UploadBytes);
    }
}

