using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.App.Views;
using Spectralis.Core.Platform;

namespace Spectralis.App;

public partial class App : Application
{
    private static bool s_exceptionLoggingInstalled;
    private readonly CancellationTokenSource _externalOpenCts = new();

    public override void Initialize()
    {
        InstallExceptionLogging();
        ConfigureEmbeddedBrowser();
        SetHighResolutionTimer();
        AvaloniaXamlLoader.Load(this);
    }

    // Set Windows multimedia timer to 1ms so DispatcherTimer fires at consistent
    // 16ms intervals instead of the default 15.6ms-multiple pattern (which causes
    // visible stutter in the frame pump at ~31ms max).
    [SupportedOSPlatform("windows")]
    private static void SetHighResolutionTimer()
    {
        if (OperatingSystem.IsWindows())
            NativeTimerMethods.TimeBeginPeriod(1);
    }

    private static class NativeTimerMethods
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        internal static extern uint TimeBeginPeriod(uint uPeriod);
    }

    private static void ConfigureEmbeddedBrowser()
    {
        // Prevent Chromium from throttling the embedded renderer.
        // disable-renderer-backgrounding / disable-background-timer-throttling:
        //   Stop the renderer process from being deprioritized when Chromium thinks
        //   it's "backgrounded" (common in windowless/OSR hosting).
        // disable-backgrounding-occluded-windows:
        //   Stop throttling when the window is partially behind other windows.
        // disable-features=CalculateNativeWinOcclusion:
        //   Disable the Windows-specific occlusion detector. When Avalonia's HWND
        //   arrangement is seen as "occluded", Chromium throttles aggressively.
        WebViewControl.WebView.Settings.AddCommandLineSwitch("disable-renderer-backgrounding", "");
        WebViewControl.WebView.Settings.AddCommandLineSwitch("disable-background-timer-throttling", "");
        WebViewControl.WebView.Settings.AddCommandLineSwitch("disable-backgrounding-occluded-windows", "");
        WebViewControl.WebView.Settings.AddCommandLineSwitch("disable-features", "CalculateNativeWinOcclusion");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppDataMigration.MigrateLegacyFolder();
        SpectralisLog.Info($"Starting Spectralis {DiagnosticsSnapshot.CurrentVersion}.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            desktop.MainWindow = mainWindow;

            // OS file-open (Explorer double-click / "Open with") passes paths as args.
            var fileArgs = (desktop.Args ?? Array.Empty<string>())
                .Where(File.Exists)
                .ToList();
            if (fileArgs.Count > 0)
            {
                SpectralisLog.Info($"Opening {fileArgs.Count} startup file argument(s).");
                _ = viewModel.OpenFilesAsync(fileArgs);
            }

            // spectralis://open?... protocol launches route through the URL opener.
            var protocolRequest = (desktop.Args ?? Array.Empty<string>())
                .Select(ExternalOpenIpc.TryParseProtocolArgument)
                .FirstOrDefault(request => request is not null);
            if (protocolRequest is not null)
            {
                _ = HandleExternalOpenRequestAsync(viewModel, mainWindow, protocolRequest);
            }

            // Plain http/https URL args (e.g. from a shell shortcut or file association).
            // Only handled when no protocol request was already found above.
            if (protocolRequest is null)
            {
                var urlArg = (desktop.Args ?? Array.Empty<string>())
                    .Select(a => a.Trim().Trim('"'))
                    .FirstOrDefault(a =>
                        Uri.TryCreate(a, UriKind.Absolute, out var u) &&
                        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps));
                if (urlArg is not null)
                {
                    SpectralisLog.Info($"Opening startup URL argument: {urlArg}");
                    _ = HandleExternalOpenRequestAsync(
                        viewModel, mainWindow,
                        new ExternalOpenRequest(ExternalOpenKind.Url, urlArg));
                }
            }

            // Preserve-session receiver: accepts open requests from second launches.
            _ = ExternalOpenIpc.RunServerAsync(
                request =>
                {
                    Dispatcher.UIThread.Post(() =>
                        _ = HandleExternalOpenRequestAsync(viewModel, mainWindow, request));
                    return Task.CompletedTask;
                },
                _externalOpenCts.Token);
            desktop.ShutdownRequested += (_, _) => _externalOpenCts.Cancel();

            // Background auto-update check — non-blocking, 8-second delay to let app settle.
            _ = RunStartupUpdateCheckAsync(viewModel, mainWindow);

            // CDN warning check — runs in parallel with the update check.
            _ = RunCdnWarningCheckAsync(mainWindow);

            // Show "Spectralis updated" dialog if a Squirrel/Velopack update was just applied.
            _ = ShowPostUpdateNoticeAsync(viewModel, mainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowPostUpdateNoticeAsync(MainWindowViewModel viewModel, MainWindow mainWindow)
    {
        var consumedVersion = viewModel.Settings.ConsumedUpdateVersion;
        if (string.IsNullOrWhiteSpace(consumedVersion)) return;
        // Wait briefly for the window to finish rendering before showing a dialog.
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await MessageWindow.ShowAsync(
                mainWindow,
                "Spectralis Updated",
                $"Your copy of Spectralis has been updated to version {consumedVersion}. Enjoy what’s new!");
        });
    }

    private static async Task RunStartupUpdateCheckAsync(MainWindowViewModel viewModel, MainWindow mainWindow)
    {
        await Task.Delay(TimeSpan.FromSeconds(8));
        var settings = AppSettingsStore.Load();

        var svc = new VelopackUpdateService();
        var checkResult = await svc.CheckForUpdateAsync(CancellationToken.None);

        // Mirror result into the feed model so the Settings view still shows version info.
        var feedResult = new ReleaseFeedResult(
            checkResult.UpdateAvailable,
            checkResult.LatestVersion,
            checkResult.ChangelogUrl ?? "https://spectralis.deltavdevs.com",
            null);
        viewModel.Settings.ApplyUpdateFeedResult(feedResult);

        if (!checkResult.UpdateAvailable) return;
        if (!string.IsNullOrWhiteSpace(viewModel.Settings.IgnoredUpdateVersion) &&
            string.Equals(viewModel.Settings.IgnoredUpdateVersion, checkResult.LatestVersion, StringComparison.OrdinalIgnoreCase)) return;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (settings.EnableAutoUpdates && checkResult.SupportsInProcessUpdate)
            {
                await UpdateProgressWindow.RunAsync(mainWindow, checkResult.LatestVersion ?? "");
            }
            else
            {
                var choice = await UpdatePromptWindow.ShowAsync(mainWindow, checkResult.LatestVersion);
                if (choice == UpdatePromptChoice.UpdateNow)
                {
                    if (checkResult.SupportsInProcessUpdate)
                        await UpdateProgressWindow.RunAsync(mainWindow, checkResult.LatestVersion ?? "");
                    else
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                            "https://spectralis.deltavdevs.com") { UseShellExecute = true });
                }
                else if (choice == UpdatePromptChoice.DontRemindAgain && !string.IsNullOrWhiteSpace(checkResult.LatestVersion))
                    viewModel.Settings.SaveIgnoredUpdateVersion(checkResult.LatestVersion);
            }
        });
    }

    private static async Task RunCdnWarningCheckAsync(MainWindow mainWindow)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));

        var settings = AppSettingsStore.Load();
        var warnings = await CdnWarningClient.FetchAsync(
            DiagnosticsSnapshot.CurrentVersion,
            settings.DismissedWarningIds);

        if (warnings.Count == 0) return;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            foreach (var warning in warnings)
            {
                var choice = await CdnWarningWindow.ShowAsync(mainWindow, warning);

                if (choice == CdnWarningChoice.ClosedApp)
                {
                    mainWindow.Close();
                    return;
                }

                if (choice == CdnWarningChoice.Dismissed && warning.Id is not null)
                {
                    settings.DismissedWarningIds.Add(warning.Id);
                    AppSettingsStore.Save(settings);
                }
            }
        });
    }

    private static async Task HandleExternalOpenRequestAsync(
        MainWindowViewModel viewModel,
        MainWindow mainWindow,
        ExternalOpenRequest request)
    {
        try
        {
            if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
            {
                mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            }

            mainWindow.Activate();

            switch (request.Kind)
            {
                case ExternalOpenKind.File when File.Exists(request.Value):
                    if (request.Intent is ExternalOpenIntent.QueueNext or ExternalOpenIntent.QueueEnd)
                    {
                        await viewModel.QueueExternalFilesAsync([request.Value], request.Intent);
                    }
                    else
                    {
                        await viewModel.OpenFilesAsync([request.Value]);
                    }

                    break;

                case ExternalOpenKind.Url when request.Intent is ExternalOpenIntent.QueueNext or ExternalOpenIntent.QueueEnd:
                    viewModel.SelectSection(viewModel.NowPlaying);
                    await viewModel.NowPlaying.QueueUrlAsync(request.Value);
                    break;

                case ExternalOpenKind.Url:
                    viewModel.SelectSection(viewModel.NowPlaying);
                    await viewModel.NowPlaying.LoadUrlAsync(request.Value);
                    break;

                case ExternalOpenKind.SharedPlay:
                    SpectralisLog.Info("Shared Play join handoff received; the Shared Play runtime is not ported yet.");
                    break;
            }
        }
        catch (Exception ex)
        {
            SpectralisLog.Error("External open request failed.", ex);
        }
    }

    private static void InstallExceptionLogging()
    {
        if (s_exceptionLoggingInstalled)
        {
            return;
        }

        s_exceptionLoggingInstalled = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                SpectralisLog.Error("Unhandled app-domain exception.", ex);
            }
            else
            {
                SpectralisLog.Error($"Unhandled app-domain exception object: {e.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            SpectralisLog.Error("Unobserved task exception.", e.Exception);
        };
    }

}
