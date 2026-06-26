using Avalonia;
using Avalonia.ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.Platform;
using Velopack;

namespace Spectralis.App;

internal static class Program
{
    // Avalonia configuration, don't remove; also used by visual designer.
    [STAThread]
    public static void Main(string[] args)
    {
        // Must be the first call — Velopack handles apply/restart before the app loads.
        VelopackApp.Build().Run();

        // TODO 5.1.0: Remove Squirrel compat block once Velopack-packaged builds are universal.
        HandleSquirrelArgsIfPresent(args);

        // Filter squirrel args so the rest of startup doesn't see them.
        args = args
            .Where(static a => !a.StartsWith("--squirrel", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Preserve session: a second launch with an open target hands the request
        // to the running instance over the named pipe and exits.
        var openRequest = BuildOpenRequest(args);
        if (openRequest is not null &&
            AppSettingsStore.Load().PreserveSession &&
            ExternalOpenIpc.TrySendAsync(openRequest).GetAwaiter().GetResult())
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static ExternalOpenRequest? BuildOpenRequest(string[] args)
    {
        foreach (var arg in args)
        {
            if (ExternalOpenIpc.TryParseProtocolArgument(arg) is { } protocolRequest)
            {
                return protocolRequest;
            }

            var value = arg.Trim().Trim('"');
            if (File.Exists(value))
            {
                return new ExternalOpenRequest(ExternalOpenKind.File, value);
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return new ExternalOpenRequest(ExternalOpenKind.Url, value);
            }
        }

        return null;
    }

    // TODO 5.1.0: Remove — handles legacy Squirrel install/update/uninstall hooks from
    // users upgrading from the WinForms release before Velopack packaging shipped.
    private static void HandleSquirrelArgsIfPresent(string[] args)
    {
        var squirrelArg = args.FirstOrDefault(static a => a.StartsWith("--squirrel", StringComparison.OrdinalIgnoreCase));
        if (squirrelArg is null)
            return;

        if (squirrelArg.Equals("--squirrel-install", StringComparison.OrdinalIgnoreCase) ||
            squirrelArg.Equals("--squirrel-updated", StringComparison.OrdinalIgnoreCase) ||
            squirrelArg.Equals("--squirrel-obsolete", StringComparison.OrdinalIgnoreCase))
        {
            // Nothing to do — shortcuts and registry entries are managed by Velopack going forward.
            Environment.Exit(0);
        }

        if (squirrelArg.Equals("--squirrel-firstrun", StringComparison.OrdinalIgnoreCase))
        {
            // First run after Squirrel install — let the app open normally.
            return;
        }

        if (squirrelArg.Equals("--squirrel-uninstall", StringComparison.OrdinalIgnoreCase))
        {
            // Best-effort cleanup of Spectralis app data on uninstall.
            try
            {
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Spectralis");
                if (Directory.Exists(appData))
                    Directory.Delete(appData, recursive: true);
            }
            catch { }
            Environment.Exit(0);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
}
