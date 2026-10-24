using System.IO;
using InternetMeter.Models;
using Microsoft.Data.Sqlite;

namespace InternetMeter.Services;

public sealed class UsageStore : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _databaseLock = new(1, 1);
    private readonly object _pendingLock = new();
    private readonly Dictionary<DateOnly, (long Download, long Upload)> _pending = [];

    public UsageStore(string? baseDirectory = null)
    {
        var root = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Internet Meter");
        Directory.CreateDirectory(root);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, "usage.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                CREATE TABLE IF NOT EXISTS daily_usage (
                    day TEXT PRIMARY KEY NOT NULL,
                    download_bytes INTEGER NOT NULL DEFAULT 0,
                    upload_bytes INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at TEXT NOT NULL,
                    ended_at TEXT NOT NULL,
                    download_bytes INTEGER NOT NULL,
                    upload_bytes INTEGER NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public void Record(long downloadBytes, long uploadBytes)
    {
        if (downloadBytes < 0 || uploadBytes < 0) return;
        lock (_pendingLock)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            _pending.TryGetValue(today, out var existing);
            _pending[today] = (existing.Download + downloadBytes, existing.Upload + uploadBytes);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<DateOnly, (long Download, long Upload)> batch;
        lock (_pendingLock)
        {
            batch = new Dictionary<DateOnly, (long Download, long Upload)>(_pending);
            _pending.Clear();
        }

        if (batch.Count == 0) return;

        var acquired = false;
        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            acquired = true;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO daily_usage(day, download_bytes, upload_bytes)
                VALUES ($day, $download, $upload)
                ON CONFLICT(day) DO UPDATE SET
                    download_bytes = download_bytes + excluded.download_bytes,
                    upload_bytes = upload_bytes + excluded.upload_bytes;
                """;
            var dayParameter = command.Parameters.Add("$day", SqliteType.Text);
            var downloadParameter = command.Parameters.Add("$download", SqliteType.Integer);
            var uploadParameter = command.Parameters.Add("$upload", SqliteType.Integer);
            foreach (var (day, values) in batch)
            {
                dayParameter.Value = day.ToString("yyyy-MM-dd");
                downloadParameter.Value = values.Download;
                uploadParameter.Value = values.Upload;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            lock (_pendingLock)
            {
                foreach (var (day, values) in batch)
                {
                    _pending.TryGetValue(day, out var existing);
                    _pending[day] = (existing.Download + values.Download, existing.Upload + values.Upload);
                }
            }
            throw;
        }
        finally
        {
            if (acquired) _databaseLock.Release();
        }
    }

    public async Task<IReadOnlyList<UsageRecord>> GetDailyUsageAsync(int days = 30, CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken);
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT day, download_bytes, upload_bytes
                FROM daily_usage
                WHERE day >= $from
                ORDER BY day DESC;
                """;
            command.Parameters.AddWithValue("$from", DateOnly.FromDateTime(DateTime.Today.AddDays(-Math.Max(1, days) + 1)).ToString("yyyy-MM-dd"));
            var records = new List<UsageRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(new UsageRecord(
                    DateOnly.Parse(reader.GetString(0)),
                    reader.GetInt64(1),
                    reader.GetInt64(2)));
            }
            return records;
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public async Task SaveSessionAsync(DateTimeOffset started, DateTimeOffset ended, long download, long upload)
    {
        await _databaseLock.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sessions(started_at, ended_at, download_bytes, upload_bytes)
                VALUES ($started, $ended, $download, $upload);
                """;
            command.Parameters.AddWithValue("$started", started.ToString("O"));
            command.Parameters.AddWithValue("$ended", ended.ToString("O"));
            command.Parameters.AddWithValue("$download", download);
            command.Parameters.AddWithValue("$upload", upload);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public async Task DeleteOlderThanAsync(int retentionDays)
    {
        await _databaseLock.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM daily_usage WHERE day < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", DateOnly.FromDateTime(DateTime.Today.AddDays(-Math.Max(30, retentionDays))).ToString("yyyy-MM-dd"));
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await FlushAsync(); } catch { }
        SqliteConnection.ClearAllPools();
        _databaseLock.Dispose();
    }
}

