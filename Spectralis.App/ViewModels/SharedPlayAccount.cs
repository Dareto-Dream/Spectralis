using System.Diagnostics;
using System.Net.Http;
using System.Reactive;
using System.Text.Json;
using ReactiveUI;
using Spectralis.Core.SharedPlay;

namespace Spectralis.App.ViewModels;

/// <summary>The one permanent Shared Play room a Ward account has.</summary>
public sealed record PlayerRoom(string Id, string Name, bool IsPublic);

/// <summary>A room in a list the Streamer Queue screen offers; Shared Play itself has just the one.</summary>
public sealed record OwnedPlayerRoom(string Id, string Name, bool IsPublic)
{
    public override string ToString() => Name;
}

/// <summary>
/// Shared Play has two kinds of room. Signed out, starting a party makes a temporary room: private, reachable
/// only through its invite link, gone when you stop. Signed in with Ward, your party always runs in your one
/// permanent room, which is created the first time you start and can be listed in the room finder.
/// </summary>
public sealed partial class SharedPlayViewModel
{
    private ReactiveCommand<Unit, Unit>? _connectWard, _disconnectWard, _manageRoom, _copyRoomLink;
    private string _accountStatus = string.Empty;
    private bool _accountBusy;
    private PlayerRoom? _myRoom;

    public string AccountStatus { get => _accountStatus; private set => this.RaiseAndSetIfChanged(ref _accountStatus, value); }
    public bool AccountBusy { get => _accountBusy; private set => this.RaiseAndSetIfChanged(ref _accountBusy, value); }
    public bool WardConnected => WardAccount.IsConnected;
    public string WardName => WardAccount.DisplayName ?? "Ward account";

    public PlayerRoom? MyRoom
    {
        get => _myRoom;
        private set
        {
            this.RaiseAndSetIfChanged(ref _myRoom, value);
            RaiseModeProps();
        }
    }

    public bool HasRoom => MyRoom is not null;
    public bool CanCopyRoomLink => HasRoom && !IsHosting;
    public string RoomName => MyRoom?.Name ?? string.Empty;
    public string RoomLink => MyRoom is null ? string.Empty : $"https://player.deltavdevs.com/rooms/{MyRoom.Id}";

    /// <summary>Two-way for the "listed in the room finder" switch.</summary>
    public bool IsRoomPublic
    {
        get => MyRoom?.IsPublic == true;
        set { if (MyRoom is not null && MyRoom.IsPublic != value) _ = SetRoomPublicAsync(value); }
    }

    public string ModeTitle => WardConnected ? "Your room" : "Temporary room";

    public string ModeText =>
        !WardConnected ? "Private and link-only. It ends when you stop and is never listed. Sign in with Ward for a permanent room."
        : MyRoom is null ? "Your permanent room is created the first time you start."
        : MyRoom.IsPublic ? "Permanent and public. Anyone can find it in the room finder."
        : "Permanent and private. Only people with your link can join.";

    private void RaiseModeProps()
    {
        this.RaisePropertyChanged(nameof(HasRoom));
        this.RaisePropertyChanged(nameof(CanCopyRoomLink));
        this.RaisePropertyChanged(nameof(RoomName));
        this.RaisePropertyChanged(nameof(RoomLink));
        this.RaisePropertyChanged(nameof(IsRoomPublic));
        this.RaisePropertyChanged(nameof(ModeTitle));
        this.RaisePropertyChanged(nameof(ModeText));
        this.RaisePropertyChanged(nameof(WardConnected));
        this.RaisePropertyChanged(nameof(WardName));
        this.RaisePropertyChanged(nameof(ShareLink));
    }

    public ReactiveCommand<Unit, Unit> ConnectWardCommand => _connectWard ??= ReactiveCommand.CreateFromTask(async () =>
    {
        if (AccountBusy) return;
        AccountBusy = true;
        AccountStatus = "Approve the connection in your browser. This window updates when you do.";
        try { await WardAccount.ConnectAsync(PlayerBase, CancellationToken.None); AccountStatus = string.Empty; await LoadMyRoomAsync(); }
        catch (OperationCanceledException) { AccountStatus = "The connection timed out. Try again when you're ready."; }
        catch (Exception ex) { AccountStatus = ex.Message; }
        finally { AccountBusy = false; RaiseModeProps(); }
    });

    public ReactiveCommand<Unit, Unit> DisconnectWardCommand => _disconnectWard ??= ReactiveCommand.CreateFromTask(async () =>
    {
        try
        {
            await WardAccount.DisconnectAsync();
            MyRoom = null;
            LiveChannelId = string.Empty;
            LiveChannelDisplayName = string.Empty;
            AccountStatus = string.Empty;
        }
        catch (Exception ex) { AccountStatus = ex.Message; }
        ReapplyControllerSettings();
        RaiseModeProps();
    });

    public ReactiveCommand<Unit, Unit> ManageRoomCommand => _manageRoom ??= ReactiveCommand.Create(() =>
    {
        if (MyRoom is not null) Process.Start(new ProcessStartInfo($"{RoomLink}/settings") { UseShellExecute = true });
    });

    public ReactiveCommand<Unit, Unit> CopyRoomLinkCommand => _copyRoomLink ??= ReactiveCommand.Create(() =>
    {
        if (string.IsNullOrEmpty(RoomLink)) return;
        CopyToClipboardRequested?.Invoke(RoomLink);
        CopyLinkLabel = "Copied!";
        _ = ResetCopyLinkLabelAsync();
    });

    private Uri PlayerBase => new(SharedPlayDefaults.NormalizeCdnBaseUrl(CdnBaseUrl) + "/");

    private async Task SetRoomPublicAsync(bool value)
    {
        if (MyRoom is null) return;
        try
        {
            await WardAccount.RequestAsync(PlayerBase, $"/player/v1/rooms/{MyRoom.Id}/profile", HttpMethod.Put, new { isPublic = value });
            MyRoom = MyRoom with { IsPublic = value };
            AccountStatus = string.Empty;
        }
        catch (Exception ex)
        {
            AccountStatus = ex.Message;
            this.RaisePropertyChanged(nameof(IsRoomPublic)); // snap the switch back
        }
    }

    /// <summary>Reads this account's permanent room, if it has one, and points hosting at it.</summary>
    private async Task LoadMyRoomAsync()
    {
        if (!WardAccount.IsConnected) { MyRoom = null; RaiseModeProps(); return; }
        try
        {
            var response = await WardAccount.RequestAsync(PlayerBase, "/player/v1/me/room", HttpMethod.Get);
            var room = response.GetProperty("room");
            if (room.ValueKind == JsonValueKind.Object)
            {
                MyRoom = new PlayerRoom(
                    room.GetProperty("id").GetString()!,
                    room.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : "My room",
                    room.TryGetProperty("isPublic", out var visible) && visible.ValueKind == JsonValueKind.True);
                LiveChannelId = MyRoom.Id;
                LiveChannelDisplayName = MyRoom.Name;
            }
            else
            {
                MyRoom = null;
                LiveChannelId = string.Empty;
            }
            AccountStatus = string.Empty;
        }
        catch (Exception ex) { AccountStatus = ex.Message; }
        ReapplyControllerSettings();
        RaiseModeProps();
    }

    /// <summary>Signed in: make sure the permanent room exists before a party starts. Signed out: nothing to do.</summary>
    private async Task EnsureRoomAsync()
    {
        if (!WardAccount.IsConnected) return;
        if (MyRoom is null) await LoadMyRoomAsync();
        if (MyRoom is not null || !WardAccount.IsConnected) return;
        try
        {
            await WardAccount.RequestAsync(PlayerBase, "/player/v1/rooms", HttpMethod.Post, new
            {
                name = $"{WardAccount.DisplayName ?? "My"}'s room",
                kind = "channel",
                security = "anyone",
                tags = Array.Empty<string>(),
                isPublic = false,
            });
        }
        catch (Exception ex) when (ex.Message.Contains("already have a room", StringComparison.OrdinalIgnoreCase))
        {
            // made on another machine a moment ago; load it below
        }
        catch (Exception ex)
        {
            AccountStatus = ex.Message;
            return;
        }
        await LoadMyRoomAsync();
    }
}
