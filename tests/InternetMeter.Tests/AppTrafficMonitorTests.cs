using System.IO.Pipes;
using System.Text.Json;
using InternetMeter.Models;
using InternetMeter.Services;

namespace InternetMeter.Tests;

public sealed class AppTrafficMonitorTests
{
    [Fact]
    public async Task HelperPublishesSnapshotsOverNamedPipe()
    {
        var pipeName = $"InternetMeter-Test-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var helperTask = AppTrafficMonitor.RunHelperAsync(pipeName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await pipe.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(pipe);
        var line = await reader.ReadLineAsync(timeout.Token);
        var snapshot = JsonSerializer.Deserialize<AppTrafficSnapshot>(Assert.IsType<string>(line));

        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Status));

        pipe.Disconnect();
        await helperTask.WaitAsync(timeout.Token);
    }
}
