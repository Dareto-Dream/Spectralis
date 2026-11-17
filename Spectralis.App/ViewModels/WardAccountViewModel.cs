using System.Net.Http;
using System.Reactive;
using System.Text.Json;
using Avalonia.Threading;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.SharedPlay;

namespace Spectralis.App.ViewModels;

public sealed class WardAccountViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings settings;
    private readonly Uri backend;
    private CancellationTokenSource? connection;
    private bool busy;
    private string status = "";
    public WardAccountViewModel(AppSettings settings)
    {
        this.settings = settings;
        backend = new Uri(SharedPlayDefaults.NormalizeCdnBaseUrl(settings.SharedPlayCdnBaseUrl));
        ConnectCommand = Command(async () =>
        {
            using var attempt = new CancellationTokenSource();
            connection = attempt;
            Status = "Approve sign-in in your browser.";
            try { await WardAccount.ConnectAsync(backend, attempt.Token); Status = "Connected."; }
            finally { connection = null; }
        });
        CancelCommand = ReactiveCommand.Create(() => connection?.Cancel());
        DisconnectCommand = Command(async () =>
        {
            try { await WardAccount.DisconnectAsync(backend); Status = "Signed out on this device."; }
            catch (Exception) { Status = "Signed out locally. The account service could not revoke this connection; remove it from Ward when you're online."; }
        });
        SaveCommand = Command(async () =>
        {
            await WardAccount.RequestAsync(backend, "/player/v1/me/preferences", HttpMethod.Put, WardPreferences.Capture(settings));
            Status = "Settings saved to Ward.";
        });
        RestoreCommand = Command(async () =>
        {
            var document = await WardAccount.RequestAsync(backend, "/player/v1/me/preferences", HttpMethod.Get);
            var preferences = document.Deserialize<WardPreferences>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("No settings have been saved yet.");
            preferences.Apply(settings);
            AppSettingsStore.Save(settings);
            Status = "Settings restored. Restart Spectralis to apply them everywhere.";
        });
        WardAccount.Changed += OnAccountChanged;
    }
    public bool Available => SharedPlayDefaults.SupportsWard(backend);
    public bool Connected => WardAccount.IsConnectedTo(backend);
    public string Name => WardAccount.GetDisplayName(backend) ?? "Ward";
    public bool Busy { get => busy; private set => this.RaiseAndSetIfChanged(ref busy, value); }
    public string Status { get => status; private set => this.RaiseAndSetIfChanged(ref status, value); }
    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> RestoreCommand { get; }
    private ReactiveCommand<Unit, Unit> Command(Func<Task> work) => ReactiveCommand.CreateFromTask(async () =>
    {
        Busy = true;
        try { await work(); }
        catch (OperationCanceledException) { Status = "Sign-in cancelled."; }
        catch (Exception ex) { Status = ex.Message; }
        finally { Busy = false; OnAccountChanged(); }
    }, this.WhenAnyValue(x => x.Busy, b => !b));
    private void OnAccountChanged() => Dispatcher.UIThread.Post(() =>
    {
        this.RaisePropertyChanged(nameof(Connected));
        this.RaisePropertyChanged(nameof(Name));
    });
    public void Dispose() { connection?.Cancel(); WardAccount.Changed -= OnAccountChanged; }
}
