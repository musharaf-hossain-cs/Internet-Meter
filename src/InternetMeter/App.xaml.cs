using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using InternetMeter.Models;
using InternetMeter.Services;
using InternetMeter.ViewModels;

namespace InternetMeter;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private SettingsService? _settingsService;
    private AppSettings? _settings;
    private UsageStore? _usageStore;
    private NetworkMonitor? _networkMonitor;
    private AppTrafficMonitor? _appTrafficMonitor;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private SharedMeterPublisher? _sharedMeterPublisher;
    private Forms.NotifyIcon? _notifyIcon;
    private Drawing.Icon? _applicationIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private DispatcherTimer? _flushTimer;
    private DispatcherTimer? _activationTimer;
    private EventWaitHandle? _showDashboardEvent;
    private bool _quotaNotificationShown;

    public bool IsExiting { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var helperArgumentIndex = Array.FindIndex(e.Args,
            argument => argument.Equals("--app-traffic-helper", StringComparison.OrdinalIgnoreCase));
        if (helperArgumentIndex >= 0 && helperArgumentIndex + 1 < e.Args.Length)
        {
            try { await AppTrafficMonitor.RunHelperAsync(e.Args[helperArgumentIndex + 1]); }
            catch (Exception exception) { AppLog.Error(exception, "Per-app traffic helper failed"); }
            Shutdown();
            return;
        }

        if (e.Args.Contains("--register-deskband", StringComparer.OrdinalIgnoreCase))
        {
            DeskbandRegistrationService.EnsureRegistered();
            Shutdown();
            return;
        }
        if (e.Args.Contains("--unregister-deskband", StringComparer.OrdinalIgnoreCase))
        {
            DeskbandRegistrationService.Unregister();
            Shutdown();
            return;
        }

        _singleInstanceMutex = new Mutex(true, @"Local\InternetMeter.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show("Internet Meter is already running.", "Internet Meter", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) AppLog.Error(exception, "Unhandled application exception");
        };

        try
        {
            var portable = e.Args.Contains("--portable", StringComparer.OrdinalIgnoreCase);
            var dataDirectory = portable ? Path.Combine(AppContext.BaseDirectory, "Data") : null;
            _settingsService = new SettingsService(dataDirectory);
            _settings = _settingsService.Load();
            _usageStore = new UsageStore(dataDirectory);
            await _usageStore.InitializeAsync();
            await _usageStore.DeleteOlderThanAsync(_settings.HistoryRetentionDays);
            _networkMonitor = new NetworkMonitor(_settings);
            _appTrafficMonitor = new AppTrafficMonitor();
            _viewModel = new MainViewModel(_settings, _usageStore, _networkMonitor, _appTrafficMonitor, Dispatcher);
            _viewModel.QuotaThresholdReached += OnQuotaThresholdReached;
            _viewModel.SpeedThresholdReached += OnSpeedThresholdReached;
            _sharedMeterPublisher = new SharedMeterPublisher();
            _viewModel.SampleProcessed += (_, _) => _sharedMeterPublisher.Publish(_viewModel, _settings);
            _sharedMeterPublisher.Publish(_viewModel, _settings);

            _mainWindow = new MainWindow(_viewModel) { Topmost = _settings.AlwaysOnTop };
            MainWindow = _mainWindow;
            DeskbandRegistrationService.EnsureRegistered();
            StartDashboardActivationListener();
            CreateTrayIcon();

            if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) _mainWindow.Show();

            _networkMonitor.Start();
            _flushTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background,
                async (_, _) =>
                {
                    try { await _usageStore.FlushAsync(); }
                    catch (Exception exception) { AppLog.Error(exception, "Failed to flush usage data"); }
                }, Dispatcher);
            _flushTimer.Start();
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Application startup failed");
            System.Windows.MessageBox.Show($"Internet Meter could not start.\n\n{exception.Message}", "Internet Meter", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.Add("Open Internet Meter", null, (_, _) => Dispatcher.Invoke(ShowDashboard));
        _trayMenu.Items.Add("Taskbar toolbar help", null, (_, _) => Dispatcher.Invoke(ShowTaskbarToolbarHelp));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(async () => await ExitApplicationAsync()));

        var iconResource = GetResourceStream(new Uri("pack://application:,,,/Assets/Brand/internet-meter.ico"));
        if (iconResource is not null)
        {
            using (iconResource.Stream)
            using (var sourceIcon = new Drawing.Icon(iconResource.Stream))
            {
                _applicationIcon = (Drawing.Icon)sourceIcon.Clone();
            }
        }

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Internet Meter",
            Icon = _applicationIcon ?? Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDashboard);
    }

    public void ShowDashboard()
    {
        if (_mainWindow is null) return;
        if (!_mainWindow.IsVisible) _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = _settings?.AlwaysOnTop ?? false;
        _mainWindow.Focus();
    }

    public void ShowTrayMenu()
    {
        if (_trayMenu is null) return;
        _trayMenu.Show(Forms.Cursor.Position);
    }

    private void ShowTaskbarToolbarHelp()
    {
        var message = DeskbandRegistrationService.IsSupported
            ? "Right-click an empty area of the Windows taskbar, open Toolbars, then click Internet Meter. Repeat the same action to hide it. If Internet Meter is not listed yet, restart Windows Explorer or sign out and back in once."
            : "Windows 11 removed the taskbar Toolbars/deskband feature. Internet Meter does not use a floating-window substitute.";
        System.Windows.MessageBox.Show(message, "Internet Meter taskbar toolbar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void StartDashboardActivationListener()
    {
        _showDashboardEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Shared.MeterSharedProtocol.ShowDashboardEventName);
        _activationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) =>
            {
                if (_showDashboardEvent.WaitOne(0)) ShowDashboard();
            }, Dispatcher);
        _activationTimer.Start();
    }

    public void ApplySettings()
    {
        if (_settings is null || _settingsService is null) return;
        try
        {
            _settingsService.Save(_settings);
            StartupService.SetEnabled(_settings.StartWithWindows);
            if (_mainWindow is not null) _mainWindow.Topmost = _settings.AlwaysOnTop;
            _sharedMeterPublisher?.Publish(_viewModel!, _settings);
            _networkMonitor?.ResetSmoothing();
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Failed to apply settings");
            System.Windows.MessageBox.Show($"Some settings could not be applied.\n\n{exception.Message}", "Internet Meter", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnQuotaThresholdReached(object? sender, int percentage)
    {
        if (_quotaNotificationShown || _notifyIcon is null) return;
        _quotaNotificationShown = true;
        _notifyIcon.BalloonTipTitle = "Internet Meter usage alert";
        _notifyIcon.BalloonTipText = $"You have used approximately {percentage}% of the configured monthly quota.";
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(6000);
    }

    private void OnSpeedThresholdReached(object? sender, double bytesPerSecond)
    {
        if (_notifyIcon is null || _settings is null) return;
        _notifyIcon.BalloonTipTitle = "Sustained network activity";
        _notifyIcon.BalloonTipText = $"Combined traffic has remained above {_settings.SpeedAlertMegabitsPerSecond:0.##} Mbps. Current rate: {SpeedFormatter.Format(bytesPerSecond, bits: true)}.";
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(6000);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error(e.Exception, "Unhandled UI exception");
        System.Windows.MessageBox.Show($"An unexpected error occurred. Internet Meter will continue running.\n\n{e.Exception.Message}",
            "Internet Meter", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private async Task ExitApplicationAsync()
    {
        if (IsExiting) return;
        IsExiting = true;
        _flushTimer?.Stop();
        _activationTimer?.Stop();
        _networkMonitor?.Dispose();
        _appTrafficMonitor?.Dispose();
        if (_usageStore is not null && _viewModel is not null)
        {
            try
            {
                await _usageStore.FlushAsync();
                await _usageStore.SaveSessionAsync(_viewModel.SessionStarted, DateTimeOffset.Now,
                    _viewModel.SessionDownloadBytes, _viewModel.SessionUploadBytes);
                await _usageStore.DisposeAsync();
            }
            catch (Exception exception) { AppLog.Error(exception, "Failed to close the usage database"); }
        }
        _notifyIcon?.Dispose();
        _trayMenu?.Dispose();
        _sharedMeterPublisher?.Dispose();
        _showDashboardEvent?.Dispose();
        _mainWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _applicationIcon?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

