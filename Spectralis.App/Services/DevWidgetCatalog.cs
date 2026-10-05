using Avalonia.Controls;
using Spectralis.App.ViewModels;
using Spectralis.App.Views;
using Spectralis.Core.Capsule;
using Spectralis.Core.Platform;
using Spectralis.Core.Playlists;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Services;

/// <summary>One window or popup the Widget Tester can open. <see cref="Spawn"/> opens it over the owner and returns a line saying what happened.</summary>
public sealed record DevWidget(string Category, string Name, string Description, Func<Window, Task<string>> Spawn)
{
    /// <summary>True if opening it can change real data when the dialog is confirmed.</summary>
    public bool TouchesRealData { get; init; }
}

/// <summary>
/// Every dialog and tool window the app can show, wired to made-up sample data so a layout can be looked at
/// without playing a track, signing in or reproducing a situation. Nothing here reads or writes the user's
/// library: the sample objects are thrown away, and the few widgets that work on real data say so.
/// </summary>
public static class DevWidgetCatalog
{
    public static IReadOnlyList<DevWidget> All { get; } = Build();

    private static List<DevWidget> Build()
    {
        const string prompts = "Prompts and dialogs";
        const string warnings = "Warnings and updates";
        const string editors = "Editors and pickers";
        const string tools = "Tool windows";
        const string info = "Info windows";

        return
        [
            new(prompts, "Confirm", "Yes / No dialog with custom button labels.", async owner =>
                $"returned {await ConfirmWindow.ShowAsync(owner, "Delete everything?", "This is only a test dialog. Nothing will be deleted.", "Delete", "Keep")}"),
            new(prompts, "Message", "Single-button message box.", async owner =>
            {
                await MessageWindow.ShowAsync(owner, "Heads up", "A message box with a title and a body.\n\nIt can run to several lines.");
                return "closed";
            }),
            new(prompts, "Name input", "Single text prompt with an initial value.", async owner =>
                $"returned {Quote(await NameInputWindow.PromptAsync(owner, "Rename playlist", "Playlist name", "Late night"))}"),
            new(prompts, "Open URL", "The Open URL prompt.", async owner =>
                $"returned {Quote(await OpenUrlWindow.PromptAsync(owner))}"),
            new(prompts, "Content warning", "The listen-anyway prompt for a tagged track.", async owner =>
                $"returned {await ContentWarningWindow.PromptAsync(owner, ["flashing lights", "explicit lyrics"], "Sample Track")}"),

            new(warnings, "Server notice (dismissible, with link)", "A dismissible warning with an action button.", async owner =>
                $"returned {await CdnWarningWindow.ShowAsync(owner, Warning("Scheduled maintenance", "Shared Play will be offline for about ten minutes.\nYour queue is kept.", "info", dismissible: true, link: true))}"),
            new(warnings, "Server notice (blocking)", "A non-dismissible warning: the title-bar close is disabled.", async owner =>
                $"returned {await CdnWarningWindow.ShowAsync(owner, Warning("Update required", "This version can no longer connect. Please update to keep using Shared Play.", "critical", dismissible: false, link: true))}"),
            new(warnings, "Server notice (warning)", "The default severity, dismissible, no link.", async owner =>
                $"returned {await CdnWarningWindow.ShowAsync(owner, Warning("Known issue", "Some capsules may stutter on export while we track this down.", "warning", dismissible: true, link: false))}"),
            new(warnings, "Update available", "Remind later / Update now / Don't remind again.", async owner =>
                $"returned {await UpdatePromptWindow.ShowAsync(owner, "9.9.9")}"),
            new(warnings, "Update status", "The update status window, on the live settings.", owner =>
            {
                new UpdateStatusWindow { DataContext = owner.DataContext }.Show(owner);
                return Task.FromResult("opened");
            }),

            new(editors, "Capsule trust", "The creator trust prompt, with an elevated capability.", async owner =>
                $"returned trusted = {await CapsuleTrustWindow.ShowAsync(owner, SampleTrust())}"),
            new(editors, "Visualizer picker", "Pick a default visualizer for a playlist.", async owner =>
            {
                var (confirmed, result) = await VisualizerPickerWindow.PromptAsync(owner,
                [
                    new VisualizerOption("Spectrum", VisualizerMode.Spectrum),
                    new VisualizerOption("Waveform", VisualizerMode.Waveform),
                    new VisualizerOption("Radial spectrum", VisualizerMode.RadialSpectrum),
                ]);
                return $"confirmed = {confirmed}, result = {(result is null ? "none" : "picked")}";
            }),
            new(editors, "Playlist picker", "Choose a playlist to add to.", async owner =>
            {
                var picked = await PlaylistPickerWindow.PromptAsync(owner,
                [
                    SampleRow("Road trip", 42, "2h 41m"), SampleRow("Focus", 120, "7h 03m"), SampleRow("Gym", 18, "58m"),
                ]);
                return $"picked {Quote(picked?.Name)}";
            }),
            new(editors, "Playlist editor", "Edit a throwaway playlist.", async owner =>
                $"saved = {await PlaylistEditorWindow.EditAsync(owner, new Playlist { Name = "Sample playlist" }, _ => [])}"),
            new(editors, "Smart playlist editor", "Edit a throwaway smart playlist with two rules.", async owner =>
                $"saved = {await SmartPlaylistEditorWindow.EditAsync(owner, SampleSmartPlaylist())}"),

            new(tools, "Notepad", "A pop-out notepad on a blank note.", owner =>
            {
                new NotepadWindow { DataContext = new NotepadViewModel() }.Show(owner);
                return Task.FromResult("opened");
            }),
            new(tools, "Wasm world test", "The wasm world test bench.", owner =>
            {
                new WasmWorldTestWindow().Show(owner);
                return Task.FromResult("opened");
            }),
            new(tools, "Scripted visualizers", "The scripted visualizer manager. Saving here changes your real scripts.", owner =>
            {
                new ScriptedVisualizerManagerWindow(_ => { }).Show(owner);
                return Task.FromResult("opened");
            }) { TouchesRealData = true },
            new(tools, "Redeem visualizer", "The redeem-a-code window.", owner =>
            {
                new RedeemVisualizerWindow().Show(owner);
                return Task.FromResult("opened");
            }) { TouchesRealData = true },
            new(tools, "Song Wars shell", "The empty Song Wars host window.", owner =>
            {
                new SongWarsWindow().Show(owner);
                return Task.FromResult("opened");
            }),
            new(tools, "Listening stats", "Reads your real scrobble history; changes nothing.", async owner =>
            {
                await new StatsWindow().ShowDialog(owner);
                return "closed";
            }),

            new(info, "About", "The About window.", async owner =>
            {
                await new AboutWindow().ShowDialog(owner);
                return "closed";
            }),
            new(info, "Terms of Service", "The terms window.", async owner =>
            {
                await new LegalDocumentWindow(LegalDocumentKind.TermsOfService).ShowDialog(owner);
                return "closed";
            }),
            new(info, "Privacy Policy", "The privacy window.", async owner =>
            {
                await new LegalDocumentWindow(LegalDocumentKind.PrivacyPolicy).ShowDialog(owner);
                return "closed";
            }),
        ];
    }

    private static string Quote(string? value) => value is null ? "nothing (cancelled)" : $"\"{value}\"";

    private static CdnWarning Warning(string title, string message, string severity, bool dismissible, bool link) => new()
    {
        Id = $"dev-widget-{Guid.NewGuid():N}",
        Title = title,
        Message = message,
        Severity = severity,
        Dismissible = dismissible,
        LinkUrl = link ? "https://spectralis.deltavdevs.com" : null,
        LinkLabel = link ? "Open site" : null,
    };

    private static CapsuleTrustContext SampleTrust() => new()
    {
        Creator = new CreatorKeyMetadata
        {
            KeyId = "dev-sample",
            Fingerprint = "AB12 CD34 EF56 7890 AB12 CD34 EF56 7890",
            DisplayName = "Sample Creator",
            Status = "active",
            AllowedCapabilities = ["sharedPlay.packageUpload"],
        },
        RequestedCapabilities = ["sharedPlay.packageUpload", "timeline.appControl", "album.world"],
        ContentTags = ["flashing lights"],
    };

    private static PlaylistRow SampleRow(string name, int tracks, string runtime) => new()
    {
        Id = Guid.NewGuid(), Name = name, TrackCount = tracks, IsSmart = false, IsSpotify = false, IsPinned = false, RuntimeText = runtime,
    };

    private static SmartPlaylist SampleSmartPlaylist() => new()
    {
        Name = "Sample smart playlist",
        Rules =
        [
            new SmartRule { Field = SmartRuleField.Genre, Op = SmartRuleOp.Is, Value = "Synthwave" },
            new SmartRule { Field = SmartRuleField.Year, Op = SmartRuleOp.GreaterThan, Value = "2015" },
        ],
    };
}
