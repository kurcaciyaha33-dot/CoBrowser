using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CefSharp;
using CefSharp.Wpf;
using CoBrowser.Auth;

namespace CoBrowser;

public partial class App : Application
{
    private const string MutexName = "Local\\CoBrowser.NativeChromium.Singleton";
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private bool _cefInitialized;
    private string? _sessionMarkerPath;
    private string _startupStage = "WPF application startup";
    private static readonly object LogSync = new();

    public static GoogleOAuthService? AuthService { get; private set; }
    public static GoogleUserProfile? CurrentGoogleUser { get; private set; }

    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoBrowser");

    public static string LogPath => Path.Combine(DataRoot, "cobrowser.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterExceptionHandlers();

        try
        {
            _startupStage = "Create CoBrowser data directory";
            Directory.CreateDirectory(DataRoot);
            Log("========== CoBrowser startup ==========");
            Log($"Version=1.0.5; BaseDirectory={AppContext.BaseDirectory}; ProcessArchitecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; OS={Environment.OSVersion}");

            _startupStage = "Acquire single-instance lock";
            _instanceMutex = new Mutex(true, MutexName, out _ownsMutex);
            if (!_ownsMutex)
            {
                MessageBox.Show("CoBrowser is already running. Use Ctrl+Shift+N to open another window.",
                    "CoBrowser", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            _startupStage = "Write session recovery marker";
            _sessionMarkerPath = Path.Combine(DataRoot, "session.running");
            var previousRunWasUnclean = File.Exists(_sessionMarkerPath);
            File.WriteAllText(_sessionMarkerPath, $"pid={Environment.ProcessId}{Environment.NewLine}started={DateTimeOffset.UtcNow:O}");

            _startupStage = "Authenticate Google account for CoBrowser profile";
            AuthService = new GoogleOAuthService(DataRoot);
            CurrentGoogleUser = AuthService.TryLoadSavedProfile();
            if (CurrentGoogleUser is null)
            {
                Log("No saved Google OAuth identity is available; showing mandatory first-run sign-in.");
                var loginWindow = new LoginWindow(AuthService);
                var loginResult = loginWindow.ShowDialog();
                if (loginResult != true || loginWindow.AuthenticatedUser is null)
                {
                    Log("Google sign-in was cancelled. CoBrowser will exit without opening the browser.");
                    Shutdown(0);
                    return;
                }
                CurrentGoogleUser = loginWindow.AuthenticatedUser;
            }
            Log($"CoBrowser profile authenticated as {CurrentGoogleUser.Email}");

            var gpuFallbackFlag = Path.Combine(DataRoot, "disable-gpu.flag");
            if (previousRunWasUnclean && !File.Exists(gpuFallbackFlag))
            {
                Log("Previous CoBrowser session did not record a clean exit.");
                var useCompatibility = MessageBox.Show(
                    "CoBrowser did not close cleanly the last time. This can happen after a graphics-driver or Chromium crash.\n\nStart this session with GPU acceleration disabled for compatibility? You can change this later in the browser menu.",
                    "CoBrowser recovery", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (useCompatibility == MessageBoxResult.Yes)
                    File.WriteAllText(gpuFallbackFlag, "User enabled GPU compatibility mode after an unclean shutdown.");
            }

            _startupStage = "Prepare Chromium profile and runtime folders";
            var userData = Path.Combine(DataRoot, "User Data");
            var defaultProfile = Path.Combine(userData, "Default");
            Directory.CreateDirectory(userData);
            Directory.CreateDirectory(defaultProfile);
            Directory.CreateDirectory(Path.Combine(DataRoot, "Viewer"));
            Directory.CreateDirectory(Path.Combine(DataRoot, "Downloads"));

            LogRuntimeFileDiagnostics();

            _startupStage = "Configure CefSharp lifecycle";
            // This must be set before the first ChromiumWebBrowser instance is created.
            // CoBrowser explicitly shuts CEF down after all WPF browser controls are disposed.
            CefSharpSettings.ShutdownOnExit = false;

            _startupStage = "Build Chromium settings";
            var settings = new CefSettings
            {
                // Keep the persistent root and Default profile directory used by CoBrowser.
                // CEF stores shared root data in User Data and browser profile data in Default.
                RootCachePath = userData,
                CachePath = defaultProfile,
                LogFile = Path.Combine(DataRoot, "cef.log"),
                LogSeverity = LogSeverity.Info,
                PersistSessionCookies = true
            };

            if (File.Exists(gpuFallbackFlag))
            {
                // CefCommandLineArgs is normally initialized by CefSettings. Guard it anyway so
                // an optional compatibility flag can never crash application startup.
                var commandLineArgs = settings.CefCommandLineArgs;
                if (commandLineArgs is not null)
                {
                    commandLineArgs["disable-gpu"] = "1";
                    commandLineArgs["disable-gpu-compositing"] = "1";
                }
                else
                {
                    Log("GPU compatibility flag exists, but CefCommandLineArgs was null; continuing with default GPU settings.");
                }
            }

            // Do not override BrowserSubprocessPath. CefSharp's package supplies and resolves
            // CefSharp.BrowserSubprocess.exe in the application output directory by default.
            Log($"CEF settings: RootCachePath={settings.RootCachePath}; CachePath={settings.CachePath}; LogFile={settings.LogFile}; GPUFallback={File.Exists(gpuFallbackFlag)}; CefAlreadyInitialized={Cef.IsInitialized}");

            _startupStage = "Initialize Chromium and validate deployed dependencies";
            var initialized = Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
            if (!initialized)
            {
                string exitCode;
                try { exitCode = Cef.GetExitCode().ToString(); }
                catch (Exception exitException)
                {
                    exitCode = "unavailable: " + exitException.Message;
                }
                throw new InvalidOperationException(
                    "Cef.Initialize returned false. CEF exit code: " + exitCode + ". Check cef.log for missing or incompatible runtime files.");
            }

            _cefInitialized = true;
            Log("CEF initialized successfully.");

            _startupStage = "Construct main browser window";
            var mainWindow = new MainWindow(AuthService!, CurrentGoogleUser!);
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            _startupStage = "Show main browser window";
            mainWindow.Show();
            Log("CoBrowser startup completed. CefSharp=" + typeof(ChromiumWebBrowser).Assembly.GetName().Version);
        }
        catch (Exception ex)
        {
            Log($"STARTUP FAILURE at stage: {_startupStage}");
            LogException(ex, "Startup");

            MessageBox.Show(
                "CoBrowser could not complete startup.\n\nStage: " + _startupStage +
                "\n\n" + ex +
                "\n\nFull details were saved to:\n" + LogPath +
                "\n\nCEF details:\n" + Path.Combine(DataRoot, "cef.log"),
                "CoBrowser startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void LogRuntimeFileDiagnostics()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            "CoBrowser.exe",
            "CefSharp.BrowserSubprocess.exe",
            "CefSharp.Core.dll",
            "CefSharp.Core.Runtime.dll",
            "CefSharp.Wpf.dll",
            "libcef.dll",
            "icudtl.dat",
            "resources.pak",
            "chrome_elf.dll"
        };

        foreach (var name in candidates)
        {
            var path = Path.Combine(baseDirectory, name);
            Log($"Runtime file: {name}; exists={File.Exists(path)}" +
                (File.Exists(path) ? $"; bytes={new FileInfo(path).Length}" : string.Empty));
        }

        var locales = Path.Combine(baseDirectory, "locales");
        Log($"Runtime locales directory: exists={Directory.Exists(locales)}; path={locales}");
    }

    private void RegisterExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogException(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()), "AppDomain");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException(args.Exception, "UnobservedTask");
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            LogException(args.Exception, "WPF Dispatcher");
            args.Handled = args.Exception is not (OutOfMemoryException or StackOverflowException or
                AccessViolationException or System.Runtime.InteropServices.SEHException);
            if (args.Handled && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                MessageBox.Show(
                    "A browser interface operation failed. CoBrowser recorded the details and will try to remain open.\n\n" +
                    args.Exception.Message + "\n\nLog: " + LogPath,
                    "CoBrowser recovered from an error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }

    public static void Log(string message)
    {
        try
        {
            lock (LogSync)
            {
                Directory.CreateDirectory(DataRoot);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 4 * 1024 * 1024)
                {
                    var previous = LogPath + ".1";
                    try { if (File.Exists(previous)) File.Delete(previous); } catch { }
                    try { File.Move(LogPath, previous); } catch { }
                }
                File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
            }
        }
        catch { /* Logging must never crash the browser. */ }
    }

    public static void LogException(Exception? exception, string source)
    {
        Log($"ERROR ({source}): {exception?.ToString() ?? "<null exception>"}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var cleanExit = !_cefInitialized;
        if (_cefInitialized)
        {
            try
            {
                Log("Application exit: beginning the single CEF shutdown sequence.");
                Cef.Shutdown();
                _cefInitialized = false;
                Log("CEF shutdown completed.");
                cleanExit = true;
            }
            catch (Exception ex)
            {
                LogException(ex, "CEF Shutdown");
            }
        }

        if (cleanExit)
        {
            try
            {
                if (_sessionMarkerPath is not null && File.Exists(_sessionMarkerPath))
                    File.Delete(_sessionMarkerPath);
            }
            catch (Exception ex) { LogException(ex, "Remove clean-exit marker"); }
        }

        if (_ownsMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
