using CefSharp;
using CefSharp.Handler;

namespace CoBrowser.Downloads;

/// <summary>Stores browser downloads in the user's CoBrowser Downloads directory.</summary>
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
            callback.Continue(path, showDialog: false);
            return true;
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Prepare browser download");
            if (!callback.IsDisposed)
            {
                callback.Continue(downloadItem.SuggestedFileName, showDialog: true);
                return true;
            }
            return false;
        }
    }

    protected override void OnDownloadUpdated(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        DownloadItem downloadItem,
        IDownloadItemCallback callback)
    {
        try { _manager.UpdateDownload(downloadItem); }
        catch (Exception ex) { App.LogException(ex, "Update browser download progress"); }
    }
}
