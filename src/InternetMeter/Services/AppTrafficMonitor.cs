using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using InternetMeter.Models;

namespace InternetMeter.Services;

public sealed class AppTrafficMonitor : IDisposable
{
    private static readonly Guid KernelNetworkProvider = new("7dd42a49-5329-4832-8dfd-43d979153a88");
    private const ulong Ipv4AndIpv6Keywords = 0x10 | 0x20;
    private static readonly HashSet<int> SendEventIds = [10, 26, 42, 58];
    private static readonly HashSet<int> ReceiveEventIds = [11, 27, 43, 59];

    private readonly object _gate = new();
    private readonly Dictionary<int, TrafficCounter> _counters = [];
    private readonly CancellationTokenSource _stopping = new();
    private TraceEventSession? _session;
    private Task? _traceTask;
    private Task? _publishingTask;
    private Task? _pipeTask;
    private Process? _helperProcess;
    private string _status = "Open the Apps tab to start real-time monitoring";
    private long _lastSnapshotTimestamp;
    private int _started;

    public event EventHandler<AppTrafficSnapshot>? SnapshotAvailable;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        if (!IsAdministrator())
        {
            StartElevatedHelper();
            return;
        }

        StartLocalTrace();
    }

    private void StartLocalTrace()
    {
        _lastSnapshotTimestamp = Stopwatch.GetTimestamp();
        _traceTask = Task.Run(TraceNetworkEvents);
        _publishingTask = Task.Run(PublishSnapshotsAsync);
    }

    private void StartElevatedHelper()
    {
        var pipeName = $"InternetMeter-AppTraffic-{Environment.ProcessId}-{Guid.NewGuid():N}";
        SetStatus("Waiting for Administrator permission…", publishImmediately: true);
        _pipeTask = Task.Run(() => ReceiveHelperSnapshotsAsync(pipeName));

        try
        {
            var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate Internet Meter executable.");
            var startInfo = new ProcessStartInfo(processPath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                startInfo.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            startInfo.ArgumentList.Add("--app-traffic-helper");
            startInfo.ArgumentList.Add(pipeName);
            _helperProcess = Process.Start(startInfo);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            SetStatus("Administrator permission was declined. Reopen Internet Meter to try again.", publishImmediately: true);
            _stopping.Cancel();
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Failed to start the per-app traffic helper");
            SetStatus("Could not start per-app monitoring.", publishImmediately: true);
            _stopping.Cancel();
        }
    }

    private async Task ReceiveHelperSnapshotsAsync(string pipeName)
    {
        try
        {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe);
            while (!_stopping.IsCancellationRequested && await reader.ReadLineAsync(_stopping.Token).ConfigureAwait(false) is { } line)
            {
                var snapshot = JsonSerializer.Deserialize<AppTrafficSnapshot>(line);
                if (snapshot is not null) SnapshotAvailable?.Invoke(this, snapshot);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception exception) when (!_stopping.IsCancellationRequested)
        {
            AppLog.Error(exception, "Per-app traffic helper connection failed");
            SetStatus("The per-app monitoring helper stopped unexpectedly.", publishImmediately: true);
        }
    }

    public static async Task RunHelperAsync(string pipeName)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(15_000).ConfigureAwait(false);
        var writer = new StreamWriter(pipe) { AutoFlush = true };
        using var monitor = new AppTrafficMonitor();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.SnapshotAvailable += (_, snapshot) =>
        {
            try { writer.WriteLine(JsonSerializer.Serialize(snapshot)); }
            catch { disconnected.TrySetResult(); }
        };
        monitor.StartLocalTrace();
        await disconnected.Task.ConfigureAwait(false);
        try { writer.Dispose(); } catch (IOException) { }
    }

    private void TraceNetworkEvents()
    {
        try
        {
            var sessionName = $"InternetMeter-AppTraffic-{Environment.ProcessId}";
            using var session = new TraceEventSession(sessionName);
            _session = session;
            session.StopOnDispose = true;
            session.BufferSizeMB = 16;
            session.Source.Dynamic.All += OnNetworkEvent;
            session.EnableProvider(KernelNetworkProvider, TraceEventLevel.Informational, Ipv4AndIpv6Keywords);
            SetStatus("Live TCP and UDP traffic by app");
            session.Source.Process();
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("Per-app monitoring needs Administrator permission. Restart Internet Meter as administrator.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            SetStatus("Per-app monitoring needs Administrator permission. Restart Internet Meter as administrator.");
        }
        catch (Exception exception) when (!_stopping.IsCancellationRequested)
        {
            AppLog.Error(exception, "Per-app network monitoring failed");
            SetStatus("Per-app monitoring is unavailable on this system.");
        }
    }

    private void OnNetworkEvent(TraceEvent data)
    {
        if (data.ProviderGuid != KernelNetworkProvider) return;

        var eventId = (int)data.ID;
        var isUpload = SendEventIds.Contains(eventId);
        if (!isUpload && !ReceiveEventIds.Contains(eventId)) return;

        var processId = ReadInt32(data, "PID");
        var size = ReadInt64(data, "size");
        if (processId < 0 || size <= 0) return;

        lock (_gate)
        {
            _counters.TryGetValue(processId, out var current);
            _counters[processId] = isUpload
                ? current with { UploadBytes = current.UploadBytes + size }
                : current with { DownloadBytes = current.DownloadBytes + size };
        }
    }

    private async Task PublishSnapshotsAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_stopping.Token).ConfigureAwait(false))
                PublishSnapshot();
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }

    private void PublishSnapshot()
    {
        Dictionary<int, TrafficCounter> counters;
        string status;
        var timestamp = Stopwatch.GetTimestamp();
        var elapsedSeconds = (timestamp - Interlocked.Exchange(ref _lastSnapshotTimestamp, timestamp)) /
                             (double)Stopwatch.Frequency;

        lock (_gate)
        {
            counters = new Dictionary<int, TrafficCounter>(_counters);
            _counters.Clear();
            status = _status;
        }

        if (elapsedSeconds <= 0 || elapsedSeconds > 10) return;

        var apps = counters
            .Where(pair => pair.Value.DownloadBytes > 0 || pair.Value.UploadBytes > 0)
            .Select(pair => new AppTrafficRate(
                GetProcessName(pair.Key),
                pair.Key,
                pair.Value.DownloadBytes / elapsedSeconds,
                pair.Value.UploadBytes / elapsedSeconds))
            .OrderByDescending(app => app.DownloadBytesPerSecond + app.UploadBytesPerSecond)
            .ToArray();

        SnapshotAvailable?.Invoke(this, new AppTrafficSnapshot(DateTimeOffset.Now, apps, status));
    }

    private string GetProcessName(int processId)
    {
        if (processId == 0) return "System Idle";
        if (processId == 4) return "System";
        try { return Process.GetProcessById(processId).ProcessName; }
        catch { return "Unavailable"; }
    }

    private void SetStatus(string status, bool publishImmediately = false)
    {
        lock (_gate) _status = status;
        if (publishImmediately)
            SnapshotAvailable?.Invoke(this, new AppTrafficSnapshot(DateTimeOffset.Now, [], status));
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int ReadInt32(TraceEvent data, string name)
    {
        try { return Convert.ToInt32(data.PayloadByName(name)); }
        catch { return -1; }
    }

    private static long ReadInt64(TraceEvent data, string name)
    {
        try { return Convert.ToInt64(data.PayloadByName(name)); }
        catch { return 0; }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        try { _session?.Dispose(); } catch { }
        try { _traceTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _publishingTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _pipeTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _helperProcess?.Dispose();
        _stopping.Dispose();
    }

    private readonly record struct TrafficCounter(long DownloadBytes, long UploadBytes);
}
