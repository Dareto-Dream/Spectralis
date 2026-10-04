using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Reactive;
using System.Text.Json;
using ReactiveUI;
using Spectralis.Core.SharedPlay;

namespace Spectralis.App.ViewModels;

public sealed record OwnedPlayerRoom(string Id, string Name, bool IsPublic)
{
    public override string ToString() => Name;
}

public sealed partial class SharedPlayViewModel
{
    private ReactiveCommand<Unit,Unit>? _connectWard, _disconnectWard, _loadRooms, _createChannel, _manageRoom, _togglePublic;
    private string _accountStatus = WardAccount.IsConnected ? $"Connected as {WardAccount.DisplayName}" : "Not connected — private parties still work.";
    private bool _accountBusy;
    private OwnedPlayerRoom? _selectedRoom;
    public string AccountStatus { get => _accountStatus; private set => this.RaiseAndSetIfChanged(ref _accountStatus,value); }
    public bool AccountBusy { get => _accountBusy; private set => this.RaiseAndSetIfChanged(ref _accountBusy,value); }
    public bool WardConnected => WardAccount.IsConnected;
    public ObservableCollection<OwnedPlayerRoom> OwnedRooms { get; } = new();
    public OwnedPlayerRoom? SelectedRoom
    {
        get => _selectedRoom;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedRoom,value);
            if (value is not null) { LiveChannelId = value.Id; LiveChannelDisplayName = value.Name; }
            this.RaisePropertyChanged(nameof(VisibilityLabel));
        }
    }
    public string VisibilityLabel => SelectedRoom?.IsPublic == true ? "Make unlisted" : "List publicly";
    public ReactiveCommand<Unit,Unit> ConnectWardCommand => _connectWard ??= ReactiveCommand.CreateFromTask(async () =>
    {
        if (AccountBusy) return;
        AccountBusy = true;
        AccountStatus = "Approve the connection in your browser. This window will update automatically.";
        try { await WardAccount.ConnectAsync(PlayerBase,CancellationToken.None); await LoadOwnedRoomsAsync(); }
        catch (OperationCanceledException) { AccountStatus = "Connection expired. Try again when you're ready."; }
        catch (Exception ex) { AccountStatus = ex.Message; }
        finally { AccountBusy = false; this.RaisePropertyChanged(nameof(WardConnected)); }
    });
    public ReactiveCommand<Unit,Unit> DisconnectWardCommand => _disconnectWard ??= ReactiveCommand.CreateFromTask(async () =>
    {
        try { LiveChannelEnabled = false; await WardAccount.DisconnectAsync(); OwnedRooms.Clear(); SelectedRoom=null; AccountStatus="Disconnected. Private parties still work."; }
        catch (Exception ex) { AccountStatus=ex.Message; }
        this.RaisePropertyChanged(nameof(WardConnected));
    });
    public ReactiveCommand<Unit,Unit> RefreshRoomsCommand => _loadRooms ??= ReactiveCommand.CreateFromTask(LoadOwnedRoomsAsync);
    public ReactiveCommand<Unit,Unit> CreateChannelCommand => _createChannel ??= ReactiveCommand.CreateFromTask(async () =>
    {
        try {
            await WardAccount.RequestAsync(PlayerBase,"/player/v1/rooms",HttpMethod.Post,new { name=$"{WardAccount.DisplayName ?? "My"} channel", kind="channel", security="anyone", tags=Array.Empty<string>(), isPublic=false });
            await LoadOwnedRoomsAsync();
        } catch (Exception ex) { AccountStatus=ex.Message; }
    });
    public ReactiveCommand<Unit,Unit> ManageRoomCommand => _manageRoom ??= ReactiveCommand.Create(() =>
    {
        if (SelectedRoom is not null) Process.Start(new ProcessStartInfo($"https://player.deltavdevs.com/rooms/{SelectedRoom.Id}/settings") { UseShellExecute=true });
    });
    public ReactiveCommand<Unit,Unit> TogglePublicCommand => _togglePublic ??= ReactiveCommand.CreateFromTask(async () =>
    {
        if (SelectedRoom is null) return;
        try {
            await WardAccount.RequestAsync(PlayerBase,$"/player/v1/rooms/{SelectedRoom.Id}/profile",HttpMethod.Put,new { isPublic=!SelectedRoom.IsPublic });
            await LoadOwnedRoomsAsync();
        } catch(Exception ex) { AccountStatus=ex.Message; }
    });
    private Uri PlayerBase => new(SharedPlayDefaults.NormalizeCdnBaseUrl(CdnBaseUrl)+"/");
    private async Task LoadOwnedRoomsAsync()
    {
        if (!WardAccount.IsConnected) return;
        try {
            var response = await WardAccount.RequestAsync(PlayerBase,"/player/v1/me/rooms",HttpMethod.Get);
            OwnedRooms.Clear();
            foreach(var room in response.GetProperty("rooms").EnumerateArray()) {
                if(room.GetProperty("kind").GetString() != "channel") continue;
                OwnedRooms.Add(new(room.GetProperty("id").GetString()!,room.GetProperty("name").GetString() ?? "My channel",room.TryGetProperty("isPublic",out var visible) && visible.ValueKind==JsonValueKind.True));
            }
            SelectedRoom=OwnedRooms.FirstOrDefault(r=>r.Id==LiveChannelId) ?? OwnedRooms.FirstOrDefault();
            AccountStatus=$"Connected as {WardAccount.DisplayName}";
            this.RaisePropertyChanged(nameof(WardConnected));
            ReapplyControllerSettings();
        } catch(Exception ex) { AccountStatus=ex.Message; }
    }
}
