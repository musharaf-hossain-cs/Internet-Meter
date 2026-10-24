using System.Globalization;
using System.IO;
using System.Text;
using InternetMeter.Models;

namespace InternetMeter.Services;

public static class CsvExporter
{
    public static async Task ExportAsync(string path, IEnumerable<UsageRecord> records)
    {
        var builder = new StringBuilder("Date,Download bytes,Upload bytes,Total bytes\r\n");
        foreach (var record in records)
        {
            builder.Append(record.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.DownloadBytes).Append(',')
                .Append(record.UploadBytes).Append(',')
                .Append(record.TotalBytes).Append("\r\n");
        }
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}

