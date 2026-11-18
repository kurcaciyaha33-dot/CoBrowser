using System.IO;
using Microsoft.Data.Sqlite;

namespace CoBrowser.Database;

public sealed record HistoryEntry(long Id, string Url, string Title, DateTimeOffset VisitedAt);

/// <summary>Small connection-per-operation SQLite history store. No long-lived connection is shared with CEF callbacks.</summary>
public sealed class HistoryStore : IDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private volatile bool _disposed;

    public string DatabasePath { get; } = Path.Combine(App.DataRoot, "Database", "browser.db");
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = true,
        DefaultTimeout = 5
    }.ToString();

    public void Initialize()
    {
        ThrowIfDisposed();
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS History (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              Url TEXT NOT NULL,
              Title TEXT NOT NULL DEFAULT '',
              VisitedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_History_VisitedAt ON History(VisitedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_History_Url ON History(Url);
            """;
        command.ExecuteNonQuery();
    }

    public async Task AddAsync(string url, string? title, CancellationToken cancellationToken = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(url)) return;
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO History(Url, Title, VisitedAt) VALUES($url, $title, $time)";
            command.Parameters.AddWithValue("$url", url);
            command.Parameters.AddWithValue("$title", title ?? string.Empty);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public IReadOnlyList<HistoryEntry> GetRecent(int limit = 100)
    {
        ThrowIfDisposed();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Url, Title, VisitedAt FROM History ORDER BY Id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        using var reader = command.ExecuteReader();
        var entries = new List<HistoryEntry>();
        while (reader.Read())
        {
            var dateText = reader.GetString(3);
            if (!DateTimeOffset.TryParse(dateText, out var visited)) visited = DateTimeOffset.MinValue;
            entries.Add(new HistoryEntry(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), visited));
        }
        return entries;
    }

    public void Clear()
    {
        ThrowIfDisposed();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM History";
        command.ExecuteNonQuery();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        // Mark the store closed, but do not dispose the gate while an async write may still
        // be unwinding through its finally/Release path. The semaphore is reclaimed with the store.
        _disposed = true;
    }
}
