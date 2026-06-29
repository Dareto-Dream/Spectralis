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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
}
