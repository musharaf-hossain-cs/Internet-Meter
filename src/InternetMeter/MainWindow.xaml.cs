using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using InternetMeter.Services;
using InternetMeter.ViewModels;
using Microsoft.Win32;

namespace InternetMeter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        _viewModel.SampleProcessed += (_, sample) => TrafficGraph.AddSample(sample.DownloadBytesPerSecond, sample.UploadBytesPerSecond);
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshIntervalCombo.SelectedValue = _viewModel.Settings.RefreshIntervalMilliseconds.ToString(CultureInfo.InvariantCulture);
        QuotaText.Text = (_viewModel.Settings.MonthlyQuotaBytes / 1_000_000_000d).ToString("0.##", CultureInfo.InvariantCulture);
        await _viewModel.RefreshUsageAsync();
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (((App)System.Windows.Application.Current).IsExiting) return;
        e.Cancel = true;
        Hide();
    }

    private async void Tabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not System.Windows.Controls.TabControl tabControl) return;

        if (tabControl.SelectedItem == UsageTab)
            await _viewModel.RefreshUsageAsync();
        else if (tabControl.SelectedItem == AppsTab)
            _viewModel.StartAppTrafficMonitoring();
        else if (tabControl.SelectedItem == ConnectionsTab)
            await _viewModel.RefreshConnectionsAsync();
    }

    private async void RefreshUsage_OnClick(object sender, RoutedEventArgs e) => await _viewModel.RefreshUsageAsync();
    private async void RefreshConnections_OnClick(object sender, RoutedEventArgs e) => await _viewModel.RefreshConnectionsAsync();

    private async void ExportCsv_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Internet Meter usage",
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"internet-meter-{DateTime.Today:yyyy-MM-dd}.csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        await CsvExporter.ExportAsync(dialog.FileName, _viewModel.UsageRows.Select(row => row.Record));
    }

    private void Stopwatch_OnClick(object sender, RoutedEventArgs e) => _viewModel.ToggleStopwatch();
    private void ResetStopwatch_OnClick(object sender, RoutedEventArgs e) => _viewModel.ResetStopwatch();

    private void SaveSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (RefreshIntervalCombo.SelectedValue is string interval && int.TryParse(interval, out var milliseconds))
            _viewModel.Settings.RefreshIntervalMilliseconds = milliseconds;

        if (double.TryParse(QuotaText.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var gigabytes))
            _viewModel.Settings.MonthlyQuotaBytes = (long)(Math.Max(0, gigabytes) * 1_000_000_000d);

        _viewModel.Settings.QuotaWarningPercent = Math.Clamp(_viewModel.Settings.QuotaWarningPercent, 1, 100);
        _viewModel.ApplyAdapterSelection();
        ((App)System.Windows.Application.Current).ApplySettings();
        System.Windows.MessageBox.Show(this, "Settings have been applied.", "Internet Meter", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

