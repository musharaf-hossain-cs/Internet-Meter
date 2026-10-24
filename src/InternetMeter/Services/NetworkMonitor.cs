using System.Diagnostics;
using System.Net.NetworkInformation;
using InternetMeter.Models;

namespace InternetMeter.Services;

public sealed class NetworkMonitor : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Dictionary<string, CounterState> _previous = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _gate = new();
    private SpeedCalculator _calculator;
    private Task? _monitoringTask;
    private long _lastTimestamp;

    public event EventHandler<NetworkSample>? SampleAvailable;

    public NetworkMonitor(AppSettings settings)
    {
        _settings = settings;
        _calculator = new SpeedCalculator(settings.SmoothingFactor);
    }

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(adapter => new NetworkAdapterInfo(
                adapter.Id,
                adapter.Name,
                adapter.Description,
                adapter.OperationalStatus == OperationalStatus.Up,
                adapter.NetworkInterfaceType.ToString()))
            .OrderByDescending(adapter => adapter.IsUp)
            .ThenBy(adapter => adapter.Name)
            .ToArray();
    }

    public void Start()
    {
        if (_monitoringTask is not null) return;
        _lastTimestamp = Stopwatch.GetTimestamp();
        CaptureBaseline();
        _monitoringTask = Task.Run(MonitorAsync);
    }

    private async Task MonitorAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                var interval = Math.Clamp(_settings.RefreshIntervalMilliseconds, 250, 5000);
                await Task.Delay(interval, _stopping.Token).ConfigureAwait(false);
                var sample = CaptureSample();
                SampleAvailable?.Invoke(this, sample);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // An adapter can disappear between enumeration and counter access.
                // The next sample will rebuild the state without terminating monitoring.
            }
        }
    }

    private void CaptureBaseline()
    {
        lock (_gate)
        {
            _previous.Clear();
            foreach (var adapter in EnumerateMonitoredAdapters())
            {
                try
                {
                    var statistics = adapter.GetIPStatistics();
                    _previous[adapter.Id] = new CounterState(statistics.BytesReceived, statistics.BytesSent);
                }
                catch (NetworkInformationException) { }
            }
        }
    }

    private NetworkSample CaptureSample()
    {
        lock (_gate)
        {
            var timestamp = Stopwatch.GetTimestamp();
            var elapsedSeconds = (timestamp - _lastTimestamp) / (double)Stopwatch.Frequency;
            _lastTimestamp = timestamp;
            if (elapsedSeconds <= 0 || elapsedSeconds > 30)
            {
                CaptureBaseline();
                _calculator.Reset();
                return NetworkSample.Empty with { Timestamp = DateTimeOffset.Now };
            }

            long receivedDelta = 0;
            long sentDelta = 0;
            var readings = new List<AdapterReading>();
            var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var adapter in EnumerateMonitoredAdapters())
            {
                activeIds.Add(adapter.Id);
                try
                {
                    var statistics = adapter.GetIPStatistics();
                    var current = new CounterState(statistics.BytesReceived, statistics.BytesSent);
                    if (!_previous.TryGetValue(adapter.Id, out var previous) ||
                        current.Received < previous.Received || current.Sent < previous.Sent)
                    {
                        _previous[adapter.Id] = current;
                        continue;
                    }

                    var adapterReceived = current.Received - previous.Received;
                    var adapterSent = current.Sent - previous.Sent;
                    receivedDelta += adapterReceived;
                    sentDelta += adapterSent;
                    readings.Add(new AdapterReading(
                        adapter.Id,
                        adapter.Name,
                        adapter.Description,
                        adapterReceived / elapsedSeconds,
                        adapterSent / elapsedSeconds,
                        current.Received,
                        current.Sent));
                    _previous[adapter.Id] = current;
                }
                catch (NetworkInformationException) { }
                catch (PlatformNotSupportedException) { }
            }

            foreach (var staleId in _previous.Keys.Where(id => !activeIds.Contains(id)).ToArray())
                _previous.Remove(staleId);

            var smoothed = _calculator.Calculate(receivedDelta, sentDelta, TimeSpan.FromSeconds(elapsedSeconds));
            return new NetworkSample(
                DateTimeOffset.Now,
                smoothed.Download,
                smoothed.Upload,
                receivedDelta,
                sentDelta,
                readings);
        }
    }

    private IEnumerable<NetworkInterface> EnumerateMonitoredAdapters()
    {
        var selected = _settings.SelectedAdapterIds;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            if (selected.Count > 0 && !selected.Contains(adapter.Id, StringComparer.OrdinalIgnoreCase))
                continue;

            if (!_settings.IncludeVirtualAdapters && IsUnwantedVirtualAdapter(adapter))
                continue;

            yield return adapter;
        }
    }

    private static bool IsUnwantedVirtualAdapter(NetworkInterface adapter)
    {
        if (adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return true;
        var description = $"{adapter.Name} {adapter.Description}";
        string[] ignoredMarkers = ["loopback", "virtualbox", "vmware", "hyper-v", "bluetooth", "wsl", "npcap"];
        return ignoredMarkers.Any(marker => description.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public void ResetSmoothing()
    {
        lock (_gate) _calculator = new SpeedCalculator(_settings.SmoothingFactor);
    }

    public void Dispose()
    {
        _stopping.Cancel();
        try { _monitoringTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _stopping.Dispose();
    }

    private sealed record CounterState(long Received, long Sent);
}

public sealed record NetworkAdapterInfo(
    string Id,
    string Name,
    string Description,
    bool IsUp,
    string Type);

