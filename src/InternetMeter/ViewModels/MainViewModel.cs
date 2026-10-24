using System.Collections.ObjectModel;
using System.Windows.Threading;
using InternetMeter.Models;
using InternetMeter.Services;

namespace InternetMeter.ViewModels;

public sealed class MainViewModel : NotifyBase
{
    private readonly AppSettings _settings;
    private readonly UsageStore _usageStore;
    private readonly NetworkMonitor _monitor;
    private readonly AppTrafficMonitor _appTrafficMonitor;
    private readonly Dispatcher _dispatcher;
    private readonly DateTimeOffset _sessionStarted = DateTimeOffset.Now;
    private string _downloadSpeed = "0.0 KB/s";
    private string _uploadSpeed = "0.0 KB/s";
    private string _sessionDownload = "0.00 B";
    private string _sessionUpload = "0.00 B";
    private string _sessionDuration = "00:00:00";
    private string _peakDownload = "0.0 KB/s";
    private string _peakUpload = "0.0 KB/s";
    private string _activeAdapter = "Waiting for network activity";
    private double _downloadRaw;
    private double _uploadRaw;
    private double _peakDownloadRaw;
    private double _peakUploadRaw;
    private long _sessionDownloadBytes;
    private long _sessionUploadBytes;
    private bool _stopwatchRunning;
    private DateTimeOffset _stopwatchStarted;
    private TimeSpan _stopwatchElapsed;
    private long _stopwatchDownloadStart;
    private long _stopwatchUploadStart;
    private string _stopwatchTime = "00:00:00.0";
    private string _stopwatchTransferred = "0.00 B";
    private string _stopwatchAverage = "0.0 KB/s";
    private DateTimeOffset? _speedAlertStarted;
    private bool _speedAlertRaised;
    private string _appTrafficStatus = "Open the Apps tab to start real-time monitoring";

    public MainViewModel(AppSettings settings, UsageStore usageStore, NetworkMonitor monitor,
        AppTrafficMonitor appTrafficMonitor, Dispatcher dispatcher)
    {
        _settings = settings;
        _usageStore = usageStore;
        _monitor = monitor;
        _appTrafficMonitor = appTrafficMonitor;
        _dispatcher = dispatcher;
        Adapters = new ObservableCollection<AdapterOption>(
            monitor.GetAdapters().Select(a => new AdapterOption(
                a,
                settings.SelectedAdapterIds.Count == 0 || settings.SelectedAdapterIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase))));
        _monitor.SampleAvailable += OnSampleAvailable;
        _appTrafficMonitor.SnapshotAvailable += OnAppTrafficSnapshotAvailable;
    }

    public event EventHandler<NetworkSample>? SampleProcessed;
    public event EventHandler<int>? QuotaThresholdReached;
    public event EventHandler<double>? SpeedThresholdReached;

    public ObservableCollection<AdapterOption> Adapters { get; }
    public ObservableCollection<UsageRowViewModel> UsageRows { get; } = [];
    public ObservableCollection<ConnectionInfo> Connections { get; } = [];
    public ObservableCollection<AppTrafficRowViewModel> AppTrafficRows { get; } = [];
    public AppSettings Settings => _settings;
    public DateTimeOffset SessionStarted => _sessionStarted;
    public long SessionDownloadBytes => _sessionDownloadBytes;
    public long SessionUploadBytes => _sessionUploadBytes;
    public string DownloadSpeed { get => _downloadSpeed; private set => Set(ref _downloadSpeed, value); }
    public string UploadSpeed { get => _uploadSpeed; private set => Set(ref _uploadSpeed, value); }
    public string SessionDownload { get => _sessionDownload; private set => Set(ref _sessionDownload, value); }
    public string SessionUpload { get => _sessionUpload; private set => Set(ref _sessionUpload, value); }
    public string SessionDuration { get => _sessionDuration; private set => Set(ref _sessionDuration, value); }
    public string PeakDownload { get => _peakDownload; private set => Set(ref _peakDownload, value); }
    public string PeakUpload { get => _peakUpload; private set => Set(ref _peakUpload, value); }
    public string ActiveAdapter { get => _activeAdapter; private set => Set(ref _activeAdapter, value); }
    public double DownloadRaw { get => _downloadRaw; private set => Set(ref _downloadRaw, value); }
    public double UploadRaw { get => _uploadRaw; private set => Set(ref _uploadRaw, value); }
    public bool StopwatchRunning { get => _stopwatchRunning; private set => Set(ref _stopwatchRunning, value); }
    public string StopwatchTime { get => _stopwatchTime; private set => Set(ref _stopwatchTime, value); }
    public string StopwatchTransferred { get => _stopwatchTransferred; private set => Set(ref _stopwatchTransferred, value); }
    public string StopwatchAverage { get => _stopwatchAverage; private set => Set(ref _stopwatchAverage, value); }
    public string AppTrafficStatus { get => _appTrafficStatus; private set => Set(ref _appTrafficStatus, value); }

    public void StartAppTrafficMonitoring() => _appTrafficMonitor.Start();

    private void OnAppTrafficSnapshotAvailable(object? sender, AppTrafficSnapshot snapshot)
    {
        _dispatcher.BeginInvoke(() =>
        {
            AppTrafficStatus = snapshot.Apps.Count == 0 && snapshot.Status.StartsWith("Live", StringComparison.Ordinal)
                ? "Live monitoring · No app traffic detected in the last second"
                : snapshot.Status;
            AppTrafficRows.Clear();
            foreach (var rate in snapshot.Apps)
                AppTrafficRows.Add(new AppTrafficRowViewModel(rate, _settings.DisplayBits));
        });
    }

    private void OnSampleAvailable(object? sender, NetworkSample sample)
    {
        _dispatcher.BeginInvoke(() => ProcessSample(sample));
    }

    private void ProcessSample(NetworkSample sample)
    {
        DownloadRaw = sample.DownloadBytesPerSecond;
        UploadRaw = sample.UploadBytesPerSecond;
        DownloadSpeed = SpeedFormatter.Format(DownloadRaw, _settings.DisplayBits);
        UploadSpeed = SpeedFormatter.Format(UploadRaw, _settings.DisplayBits);
        _peakDownloadRaw = Math.Max(_peakDownloadRaw, DownloadRaw);
        _peakUploadRaw = Math.Max(_peakUploadRaw, UploadRaw);
        PeakDownload = SpeedFormatter.Format(_peakDownloadRaw, _settings.DisplayBits);
        PeakUpload = SpeedFormatter.Format(_peakUploadRaw, _settings.DisplayBits);
        _sessionDownloadBytes += sample.DownloadBytesDelta;
        _sessionUploadBytes += sample.UploadBytesDelta;
        SessionDownload = SpeedFormatter.FormatBytes(_sessionDownloadBytes);
        SessionUpload = SpeedFormatter.FormatBytes(_sessionUploadBytes);
        SessionDuration = (DateTimeOffset.Now - _sessionStarted).ToString(@"dd\.hh\:mm\:ss").TrimStart('0', '.');
        if (SessionDuration.Length < 8) SessionDuration = (DateTimeOffset.Now - _sessionStarted).ToString(@"hh\:mm\:ss");
        ActiveAdapter = sample.Adapters.Count switch
        {
            0 => "No active traffic",
            1 => sample.Adapters[0].Name,
            _ => $"{sample.Adapters.Count} active adapters"
        };

        _usageStore.Record(sample.DownloadBytesDelta, sample.UploadBytesDelta);
        CheckSpeedAlert(sample);
        UpdateStopwatch();
        SampleProcessed?.Invoke(this, sample);
    }

    private void CheckSpeedAlert(NetworkSample sample)
    {
        var threshold = _settings.SpeedAlertMegabitsPerSecond * 1_000_000d / 8d;
        if (threshold <= 0)
        {
            _speedAlertStarted = null;
            _speedAlertRaised = false;
            return;
        }

        var speed = sample.DownloadBytesPerSecond + sample.UploadBytesPerSecond;
        if (speed >= threshold)
        {
            _speedAlertStarted ??= sample.Timestamp;
            if (!_speedAlertRaised &&
                sample.Timestamp - _speedAlertStarted.Value >= TimeSpan.FromSeconds(Math.Clamp(_settings.SpeedAlertDurationSeconds, 1, 3600)))
            {
                _speedAlertRaised = true;
                SpeedThresholdReached?.Invoke(this, speed);
            }
        }
        else if (speed < threshold * 0.75)
        {
            _speedAlertStarted = null;
            _speedAlertRaised = false;
        }
    }

    public async Task RefreshUsageAsync()
    {
        var records = await _usageStore.GetDailyUsageAsync(62);
        UsageRows.Clear();
        foreach (var record in records) UsageRows.Add(new UsageRowViewModel(record));
        CheckQuota(records);
    }

    public async Task RefreshConnectionsAsync()
    {
        var connections = await Task.Run(ConnectionViewerService.GetTcpConnections);
        Connections.Clear();
        foreach (var connection in connections) Connections.Add(connection);
    }

    private void CheckQuota(IReadOnlyList<UsageRecord> records)
    {
        if (!_settings.QuotaNotificationsEnabled || _settings.MonthlyQuotaBytes <= 0) return;
        var monthStart = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var used = records.Where(record => record.Day >= monthStart).Sum(record => record.TotalBytes);
        var percentage = (int)Math.Floor(used * 100d / _settings.MonthlyQuotaBytes);
        if (percentage >= _settings.QuotaWarningPercent)
            QuotaThresholdReached?.Invoke(this, percentage);
    }

    public void ToggleStopwatch()
    {
        if (!StopwatchRunning)
        {
            StopwatchRunning = true;
            _stopwatchStarted = DateTimeOffset.Now;
            _stopwatchDownloadStart = _sessionDownloadBytes;
            _stopwatchUploadStart = _sessionUploadBytes;
            _stopwatchElapsed = TimeSpan.Zero;
        }
        else
        {
            _stopwatchElapsed += DateTimeOffset.Now - _stopwatchStarted;
            StopwatchRunning = false;
        }
        UpdateStopwatch();
    }

    public void ResetStopwatch()
    {
        StopwatchRunning = false;
        _stopwatchElapsed = TimeSpan.Zero;
        _stopwatchDownloadStart = _sessionDownloadBytes;
        _stopwatchUploadStart = _sessionUploadBytes;
        StopwatchTime = "00:00:00.0";
        StopwatchTransferred = "0.00 B";
        StopwatchAverage = "0.0 KB/s";
    }

    private void UpdateStopwatch()
    {
        var elapsed = _stopwatchElapsed + (StopwatchRunning ? DateTimeOffset.Now - _stopwatchStarted : TimeSpan.Zero);
        var transferred = Math.Max(0, _sessionDownloadBytes - _stopwatchDownloadStart) +
                          Math.Max(0, _sessionUploadBytes - _stopwatchUploadStart);
        StopwatchTime = elapsed.ToString(@"hh\:mm\:ss\.f");
        StopwatchTransferred = SpeedFormatter.FormatBytes(transferred);
        StopwatchAverage = SpeedFormatter.Format(elapsed.TotalSeconds > 0 ? transferred / elapsed.TotalSeconds : 0, _settings.DisplayBits);
    }

    public void ApplyAdapterSelection()
    {
        var selected = Adapters.Where(adapter => adapter.IsSelected).Select(adapter => adapter.Id).ToList();
        _settings.SelectedAdapterIds = selected.Count == Adapters.Count ? [] : selected;
        _monitor.ResetSmoothing();
    }
}

