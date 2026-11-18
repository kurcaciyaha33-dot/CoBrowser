using CefSharp;
using CefSharp.Handler;

namespace CoBrowser.Services;

/// <summary>Records Chromium renderer crashes so they can be distinguished from a full CoBrowser process exit.</summary>
public sealed class CrashRecoveryRequestHandler(Action<string> onRendererTerminated) : RequestHandler
{
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
