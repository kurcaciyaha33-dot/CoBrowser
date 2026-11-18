using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using CefSharp;

namespace CoBrowser.Downloads;

/// <summary>Tracks downloads created by CefSharp and exposes UI-safe progress information.</summary>
public sealed class DownloadManager
{
    private readonly string _downloadDirectory;
    private readonly string _historyPath;
    private readonly object _sync = new();
    private readonly Dictionary<int, string> _paths = new();
    private readonly Dictionary<int, DownloadInfo> _items = new();
    private readonly HashSet<string> _reservedPaths = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<DownloadInfo> Downloads { get; } = new();
    public string DownloadDirectory => _downloadDirectory;

    public DownloadManager(string downloadDirectory)
    {
        _downloadDirectory = Path.GetFullPath(downloadDirectory);
        Directory.CreateDirectory(_downloadDirectory);
        _historyPath = Path.Combine(App.DataRoot, "download-history.json");
        LoadCompletedHistory();
    }

    public string PrepareDownload(DownloadItem item)
    {
        string path;
        lock (_sync)
        {
            var fileName = SanitizeFileName(item.SuggestedFileName);
            path = GetUniquePath(fileName);
            _paths[item.Id] = path;
        }

        var snapshot = DownloadSnapshot.From(item, path);
        Dispatch(() => Upsert(snapshot));
        App.Log($"Download started: id={item.Id}; file={path}; url={item.Url}");
        return path;
    }

    public void UpdateDownload(DownloadItem item)
    {
        string path;
        lock (_sync)
        {
            if (!_paths.TryGetValue(item.Id, out path!))
            {
                path = GetUniquePath(SanitizeFileName(item.SuggestedFileName));
                _paths[item.Id] = path;
            }
        }

        var snapshot = DownloadSnapshot.From(item, path);
        Dispatch(() =>
        {
            Upsert(snapshot);
            if (snapshot.IsComplete) SaveCompletedHistory();
        });
        if (item.IsComplete)
            App.Log($"Download completed: id={item.Id}; file={path}");
        else if (item.IsCancelled)
            App.Log($"Download cancelled: id={item.Id}; file={path}");
    }

    public void Remove(DownloadInfo? item)
    {
        if (item is null) return;
        Dispatch(() =>
        {
            Downloads.Remove(item);
            lock (_sync)
            {
                _items.Remove(item.Id);
                _paths.Remove(item.Id);
                _reservedPaths.Remove(item.FullPath);
            }
            SaveCompletedHistory();
        });
    }

    public void ClearCompleted()
    {
        Dispatch(() =>
        {
            var completed = Downloads.Where(x => x.IsComplete || x.IsCancelled).ToArray();
            foreach (var item in completed)
            {
                Downloads.Remove(item);
                lock (_sync)
                {
                    _items.Remove(item.Id);
                    _paths.Remove(item.Id);
                    _reservedPaths.Remove(item.FullPath);
                }
            }
            SaveCompletedHistory();
        });
    }

    private void LoadCompletedHistory()
    {
        try
        {
            if (!File.Exists(_historyPath)) return;
            var stored = JsonSerializer.Deserialize<List<StoredDownload>>(File.ReadAllText(_historyPath)) ?? new();
            var id = -1;
            foreach (var entry in stored.OrderByDescending(x => x.CompletedAt).Take(200))
            {
                var path = Path.GetFullPath(entry.FilePath);
                if (!IsInsideDownloadDirectory(path) || !File.Exists(path)) continue;
                var info = new DownloadInfo(id--, path);
                info.RestoreCompleted(entry.Url, new FileInfo(path).Length, entry.CompletedAt);
                Downloads.Add(info);
                _items[info.Id] = info;
            }
        }
        catch (Exception ex) { App.LogException(ex, "Load download history"); }
    }

    private void SaveCompletedHistory()
    {
        try
        {
            var history = Downloads
                .Where(x => x.IsComplete && File.Exists(x.FullPath) && IsInsideDownloadDirectory(x.FullPath))
                .OrderByDescending(x => x.CompletedAt)
                .Take(200)
                .Select(x => new StoredDownload(x.FullPath, x.Url, x.ReceivedBytes, x.CompletedAt))
                .ToList();
            var temporary = _historyPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _historyPath, overwrite: true);
        }
        catch (Exception ex) { App.LogException(ex, "Save download history"); }
    }

    private bool IsInsideDownloadDirectory(string path)
    {
        var prefix = _downloadDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record StoredDownload(string FilePath, string Url, long Size, DateTimeOffset CompletedAt);

    private void Upsert(DownloadSnapshot snapshot)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => Upsert(snapshot)));
            return;
        }

        DownloadInfo entry;
        lock (_sync)
        {
            if (!_items.TryGetValue(snapshot.Id, out entry!))
            {
                entry = new DownloadInfo(snapshot.Id, snapshot.FilePath);
                _items[snapshot.Id] = entry;
                Downloads.Insert(0, entry);
            }
        }
        entry.Apply(snapshot.Url, snapshot.ReceivedBytes, snapshot.TotalBytes, snapshot.CurrentSpeed,
            snapshot.PercentComplete, snapshot.IsInProgress, snapshot.IsComplete, snapshot.IsCancelled);
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private string GetUniquePath(string fileName)
    {
        var candidate = Path.Combine(_downloadDirectory, fileName);
        if (!File.Exists(candidate) && !File.Exists(candidate + ".crdownload") && _reservedPaths.Add(candidate)) return candidate;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 1; index < 10000; index++)
        {
            candidate = Path.Combine(_downloadDirectory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate) && !File.Exists(candidate + ".crdownload") && _reservedPaths.Add(candidate)) return candidate;
        }
        candidate = Path.Combine(_downloadDirectory, $"{Guid.NewGuid():N}{extension}");
        _reservedPaths.Add(candidate);
        return candidate;
    }

    private static string SanitizeFileName(string? suggestedName)
    {
        var value = string.IsNullOrWhiteSpace(suggestedName) ? "download" : (Path.GetFileName(suggestedName.Trim()) ?? "download");
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        value = value.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(value)) value = "download";
        return value.Length > 180 ? value[..180] : value;
    }

    private sealed record DownloadSnapshot(
        int Id,
        string FilePath,
        string Url,
        long ReceivedBytes,
        long TotalBytes,
        long CurrentSpeed,
        int PercentComplete,
        bool IsInProgress,
        bool IsComplete,
        bool IsCancelled)
    {
        public static DownloadSnapshot From(DownloadItem item, string path) => new(
            item.Id,
            path,
            item.Url ?? string.Empty,
            item.ReceivedBytes,
            item.TotalBytes,
            item.CurrentSpeed,
            item.PercentComplete,
            item.IsInProgress,
            item.IsComplete,
            item.IsCancelled);
    }
}

public sealed class DownloadInfo : INotifyPropertyChanged
{
    private string _url = string.Empty;
    private long _receivedBytes;
    private long _totalBytes;
    private long _currentSpeed;
    private int _percentComplete;
    private bool _isInProgress;
    private bool _isComplete;
    private bool _isCancelled;

    public int Id { get; }
    public string FullPath { get; }
    public string FileName { get; }
    public string Url { get => _url; private set => Set(ref _url, value); }
    public long ReceivedBytes { get => _receivedBytes; private set => Set(ref _receivedBytes, value); }
    public long TotalBytes { get => _totalBytes; private set => Set(ref _totalBytes, value); }
    public long CurrentSpeed { get => _currentSpeed; private set => Set(ref _currentSpeed, value); }
    public int PercentComplete { get => _percentComplete; private set => Set(ref _percentComplete, Math.Clamp(value, 0, 100)); }
    public bool IsInProgress { get => _isInProgress; private set => Set(ref _isInProgress, value); }
    public bool IsComplete { get => _isComplete; private set => Set(ref _isComplete, value); }
    public bool IsCancelled { get => _isCancelled; private set => Set(ref _isCancelled, value); }
    public bool CanOpen => IsComplete && File.Exists(FullPath);
    public bool IsIndeterminate => IsInProgress && TotalBytes <= 0;

    public string StatusText
    {
        get
        {
            if (IsComplete) return $"Completed · {FormatBytes(ReceivedBytes)}";
            if (IsCancelled) return "Cancelled";
            if (IsInProgress)
            {
                var progress = TotalBytes > 0 ? $"{PercentComplete}% · " : string.Empty;
                var speed = CurrentSpeed > 0 ? $" · {FormatBytes(CurrentSpeed)}/s" : string.Empty;
                return $"Downloading · {progress}{FormatBytes(ReceivedBytes)}{(TotalBytes > 0 ? $" / {FormatBytes(TotalBytes)}" : string.Empty)}{speed}";
            }
            return "Starting…";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DownloadInfo(int id, string fullPath)
    {
        Id = id;
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath) ?? "download";
    }

    internal void RestoreCompleted(string url, long size, DateTimeOffset completedAt)
    {
        Apply(url, size, size, 0, 100, false, true, false);
        CompletedAt = completedAt;
    }

    public DateTimeOffset CompletedAt { get; private set; }

    internal void Apply(string url, long receivedBytes, long totalBytes, long currentSpeed,
        int percentComplete, bool isInProgress, bool isComplete, bool isCancelled)
    {
        Url = url;
        ReceivedBytes = receivedBytes;
        TotalBytes = totalBytes;
        CurrentSpeed = currentSpeed;
        PercentComplete = percentComplete;
        IsInProgress = isInProgress;
        if (isComplete && !IsComplete) CompletedAt = DateTimeOffset.UtcNow;
        IsComplete = isComplete;
        IsCancelled = isCancelled;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(IsIndeterminate));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
