using CefSharp;
using CefSharp.Handler;

namespace CoBrowser.Downloads;

/// <summary>Writes Chromium downloads directly to the signed-in Windows user's Downloads folder.</summary>
public sealed class CoBrowserDownloadHandler : DownloadHandler
{
    private readonly DownloadManager _manager;

    public CoBrowserDownloadHandler(DownloadManager manager) => _manager = manager;

    protected override bool OnBeforeDownload(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        DownloadItem downloadItem,
        IBeforeDownloadCallback callback)
    {
        if (callback.IsDisposed) return false;
        try
        {
            var path = _manager.PrepareDownload(downloadItem);
            App.Log($"Accepting download: {downloadItem.SuggestedFileName} -> {path}");
            using (callback)
            {
                callback.Continue(path, showDialog: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Prepare browser download");
            // Do not fall back to a relative filename/current working directory.
            // A failed target selection is reported and the download is cancelled safely.
            return false;
        }
    }

    protected override void OnDownloadUpdated(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        DownloadItem downloadItem,
        IDownloadItemCallback callback)
    {
        try
        {
            _manager.UpdateDownload(downloadItem);
            if (downloadItem.IsComplete)
                App.Log($"Download completed: {downloadItem.SuggestedFileName}; bytes={downloadItem.ReceivedBytes}");
            else if (downloadItem.IsCancelled)
                App.Log($"Download cancelled: {downloadItem.SuggestedFileName}");
        }
        catch (Exception ex) { App.LogException(ex, "Update browser download progress"); }
    }
}
