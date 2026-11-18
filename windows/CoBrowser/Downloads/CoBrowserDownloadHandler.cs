using CefSharp;
using CefSharp.Handler;

namespace CoBrowser.Downloads;

/// <summary>Downloads directly to the current Windows user's Downloads folder.</summary>
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
            App.Log($"CEF accepted download destination: {path}; id={downloadItem.Id}");
            return true;
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Prepare browser download; download was not silently redirected");
            // Do not pass a bare filename to CEF: that can place the file in an unexpected
            // working directory. Returning false makes the failure explicit in the CEF log.
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
