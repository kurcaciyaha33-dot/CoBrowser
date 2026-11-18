using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace CoBrowser.Downloads;

public partial class DownloadsPage : UserControl, IDisposable
{
    private readonly DownloadManager _manager;
    private bool _disposed;

    public DownloadsPage(DownloadManager manager)
    {
        InitializeComponent();
        _manager = manager;
        DataContext = manager;
        DownloadsList.ItemsSource = manager.Downloads;
        manager.Downloads.CollectionChanged += Downloads_CollectionChanged;
        UpdateEmptyState();
    }

    private void Downloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manager.Downloads.CollectionChanged -= Downloads_CollectionChanged;
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = _manager.Downloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadCountText.Text = _manager.Downloads.Count == 1
            ? "1 item"
            : $"{_manager.Downloads.Count} items";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(_manager.DownloadDirectory);

    private void ClearCompleted_Click(object sender, RoutedEventArgs e) => _manager.ClearCompleted();

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var path = (sender as FrameworkElement)?.Tag as string;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { App.LogException(ex, "Open downloaded file"); ShowError("Could not open this file.", ex); }
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = (sender as FrameworkElement)?.Tag as string;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                OpenFolder(_manager.DownloadDirectory);
        }
        catch (Exception ex) { App.LogException(ex, "Show downloaded file in folder"); ShowError("Could not open the download location.", ex); }
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { App.LogException(ex, "Open downloads folder"); ShowError("Could not open the downloads folder.", ex); }
    }

    private static void ShowError(string title, Exception ex) =>
        MessageBox.Show(title + "\n\n" + ex.Message, "CoBrowser Downloads", MessageBoxButton.OK, MessageBoxImage.Warning);
}
