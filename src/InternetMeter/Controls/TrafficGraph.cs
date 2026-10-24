using System.Windows;
using System.Windows.Media;

namespace InternetMeter.Controls;

public sealed class TrafficGraph : FrameworkElement
{
    private const int Capacity = 120;
    private readonly Queue<(double Download, double Upload)> _samples = new();
    private readonly System.Windows.Media.Pen _downloadPen = new(new SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248)), 2);
    private readonly System.Windows.Media.Pen _uploadPen = new(new SolidColorBrush(System.Windows.Media.Color.FromRgb(167, 139, 250)), 2);

    public void AddSample(double download, double upload)
    {
        _samples.Enqueue((Math.Max(0, download), Math.Max(0, upload)));
        while (_samples.Count > Capacity) _samples.Dequeue();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42)), null, bounds, 8, 8);

        var gridPen = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(75, 71, 85, 105)), 1);
        for (var i = 1; i < 4; i++)
        {
            var y = ActualHeight * i / 4;
            drawingContext.DrawLine(gridPen, new System.Windows.Point(0, y), new System.Windows.Point(ActualWidth, y));
        }

        if (_samples.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var values = _samples.ToArray();
        var max = Math.Max(1024d, values.Max(point => Math.Max(point.Download, point.Upload)) * 1.15);
        DrawSeries(drawingContext, values.Select(value => value.Download).ToArray(), max, _downloadPen);
        DrawSeries(drawingContext, values.Select(value => value.Upload).ToArray(), max, _uploadPen);
    }

    private void DrawSeries(DrawingContext drawingContext, double[] values, double max, System.Windows.Media.Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var xStep = ActualWidth / Math.Max(1, Capacity - 1);
            var startX = ActualWidth - (values.Length - 1) * xStep;
            context.BeginFigure(new System.Windows.Point(startX, ActualHeight - values[0] / max * ActualHeight), false, false);
            for (var i = 1; i < values.Length; i++)
            {
                context.LineTo(new System.Windows.Point(startX + i * xStep, ActualHeight - values[i] / max * ActualHeight), true, false);
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}

