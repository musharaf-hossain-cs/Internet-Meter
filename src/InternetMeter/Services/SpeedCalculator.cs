namespace InternetMeter.Services;

public sealed class SpeedCalculator(double smoothingFactor = 0.35)
{
    private bool _initialized;
    private double _download;
    private double _upload;

    public (double Download, double Upload) Calculate(long receivedDelta, long sentDelta, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero || receivedDelta < 0 || sentDelta < 0)
            return (_download, _upload);

        var rawDownload = receivedDelta / elapsed.TotalSeconds;
        var rawUpload = sentDelta / elapsed.TotalSeconds;
        var factor = Math.Clamp(smoothingFactor, 0, 1);

        if (!_initialized)
        {
            _download = rawDownload;
            _upload = rawUpload;
            _initialized = true;
        }
        else
        {
            _download = factor * rawDownload + (1 - factor) * _download;
            _upload = factor * rawUpload + (1 - factor) * _upload;
        }

        return (_download, _upload);
    }

    public void Reset()
    {
        _initialized = false;
        _download = 0;
        _upload = 0;
    }
}

