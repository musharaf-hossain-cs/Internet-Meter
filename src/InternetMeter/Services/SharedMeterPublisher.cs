using System.IO.MemoryMappedFiles;
using System.Text;
using InternetMeter.Models;
using InternetMeter.Shared;
using InternetMeter.ViewModels;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace InternetMeter.Services;

public sealed class SharedMeterPublisher : IDisposable
{
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private readonly Mutex _mutex;

    public SharedMeterPublisher()
    {
        _map = MemoryMappedFile.CreateOrOpen(MeterSharedProtocol.MapName, MeterSharedProtocol.Capacity);
        _view = _map.CreateViewAccessor(0, MeterSharedProtocol.Capacity, MemoryMappedFileAccess.ReadWrite);
        _mutex = new Mutex(false, MeterSharedProtocol.MutexName);
    }

    public void Publish(MainViewModel viewModel, AppSettings settings)
    {
        var lockTaken = false;
        try
        {
            try { lockTaken = _mutex.WaitOne(TimeSpan.FromMilliseconds(50)); }
            catch (AbandonedMutexException) { lockTaken = true; }
            if (!lockTaken) return;

            _view.Write(MeterSharedProtocol.MagicOffset, MeterSharedProtocol.Magic);
            _view.Write(MeterSharedProtocol.VersionOffset, MeterSharedProtocol.Version);
            _view.Write(MeterSharedProtocol.TimestampOffset, DateTime.UtcNow.Ticks);
            WriteText(MeterSharedProtocol.DownloadLengthOffset, MeterSharedProtocol.DownloadTextOffset, viewModel.DownloadSpeed);
            WriteText(MeterSharedProtocol.UploadLengthOffset, MeterSharedProtocol.UploadTextOffset, viewModel.UploadSpeed);
            _view.Write(MeterSharedProtocol.CompactOffset, settings.CompactSingleLine ? 1 : 0);
            _view.Write(MeterSharedProtocol.DownloadColorOffset, ToArgb(settings.DownloadColor, MediaColor.FromRgb(56, 189, 248)));
            _view.Write(MeterSharedProtocol.UploadColorOffset, ToArgb(settings.UploadColor, MediaColor.FromRgb(167, 139, 250)));
            _view.Write(MeterSharedProtocol.BackgroundColorOffset, ToArgb(settings.MeterBackground, MediaColor.FromRgb(32, 32, 32)));
            _view.Flush();
        }
        finally
        {
            if (lockTaken) _mutex.ReleaseMutex();
        }
    }

    private void WriteText(int lengthOffset, int textOffset, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = Math.Min(bytes.Length, MeterSharedProtocol.TextCapacity);
        _view.Write(lengthOffset, length);
        _view.WriteArray(textOffset, bytes, 0, length);
    }

    private static int ToArgb(string value, MediaColor fallback)
    {
        MediaColor color;
        try { color = (MediaColor)MediaColorConverter.ConvertFromString(value); }
        catch { color = fallback; }
        return unchecked((int)(((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B));
    }

    public void Dispose()
    {
        _view.Dispose();
        _map.Dispose();
        _mutex.Dispose();
    }
}
