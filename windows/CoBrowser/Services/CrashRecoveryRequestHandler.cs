using CefSharp;
using CefSharp.Handler;

namespace CoBrowser.Services;

/// <summary>Routes Chromium target=_blank/window.open requests into CoBrowser tabs and records renderer crashes.</summary>
public sealed class CrashRecoveryRequestHandler(
    Action<string> onRendererTerminated,
    Action<string> openInNewTab) : RequestHandler
{
    protected override bool OnOpenUrlFromTab(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string targetUrl,
        WindowOpenDisposition targetDisposition,
        bool userGesture)
    {
        if (string.IsNullOrWhiteSpace(targetUrl)) return true;
        try { openInNewTab(targetUrl); }
        catch (Exception ex) { App.LogException(ex, "Open popup URL in new tab"); }
        // Returning true prevents Chromium from creating a separate native window.
        return true;
    }

    protected override void OnRenderProcessTerminated(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        CefTerminationStatus status,
        int errorCode,
        string errorMessage)
    {
        var description = $"{status}; code={errorCode}; detail={errorMessage}";
        string? url = null;
        try { url = browser.MainFrame?.Url; }
        catch (Exception ex) { App.LogException(ex, "Read renderer crash URL"); }

        App.Log($"Chromium renderer terminated unexpectedly: {description}; URL={url}");
        try { onRendererTerminated(description); }
        catch (Exception ex) { App.LogException(ex, "Renderer termination callback"); }
    }
}
