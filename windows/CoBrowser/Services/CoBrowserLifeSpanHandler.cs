using CefSharp;
using CefSharp.Handler;

namespace CoBrowser.Services;

/// <summary>Turns Chromium popups/window.open/target=_blank requests into CoBrowser tabs.</summary>
public sealed class CoBrowserLifeSpanHandler : LifeSpanHandler
{
    private readonly Action<string> _openInTab;

    public CoBrowserLifeSpanHandler(Action<string> openInTab) =>
        _openInTab = openInTab ?? throw new ArgumentNullException(nameof(openInTab));

    protected override bool OnBeforePopup(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string targetUrl,
        string targetFrameName,
        WindowOpenDisposition targetDisposition,
        bool userGesture,
        IPopupFeatures popupFeatures,
        IWindowInfo windowInfo,
        IBrowserSettings browserSettings,
        ref bool noJavascriptAccess,
        out IWebBrowser newBrowser)
    {
        newBrowser = null!;
        if (!string.IsNullOrWhiteSpace(targetUrl))
            _openInTab(targetUrl);
        return true; // Cancel the native popup; CoBrowser opens the URL in a tab instead.
    }
}
