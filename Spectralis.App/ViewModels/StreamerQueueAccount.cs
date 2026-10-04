using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Reactive;
using ReactiveUI;
using Spectralis.Core.SharedPlay;

namespace Spectralis.App.ViewModels;

public sealed partial class StreamerQueueViewModel
{
    private ReactiveCommand<Unit,Unit>? _linkWard, _reloadWard, _useWardQueue;
    private bool _wardBusy;
    public ObservableCollection<OwnedPlayerRoom> WardQueues { get; } = new();
    private OwnedPlayerRoom? _wardQueue;
    public OwnedPlayerRoom? WardQueue { get => _wardQueue; set=>this.RaiseAndSetIfChanged(ref _wardQueue,value); }
    public ReactiveCommand<Unit,Unit> LinkWardQueueCommand => _linkWard ??= ReactiveCommand.CreateFromTask(async ()=>
    {
        if(_wardBusy)return;
        _wardBusy=true;
        try {
            if(!WardAccount.IsConnected){StatusText="Approve the Ward connection in your browser.";await WardAccount.ConnectAsync(_cdnBaseUri,CancellationToken.None);}
            if(HasRoom && _settings is not null){
                await WardAccount.RequestAsync(_cdnBaseUri,$"/player/v1/queues/{RoomId}/claim",HttpMethod.Post,new{ownerToken=_settings.SqOwnerToken});
                Process.Start(new ProcessStartInfo($"https://player.deltavdevs.com/rooms/{RoomId}/settings"){UseShellExecute=true});
            }
            await LoadWardQueuesAsync();
            StatusText=$"Connected as {WardAccount.DisplayName}";
        } catch(Exception ex){LastError=ex.Message;} finally{_wardBusy=false;}
    });
    public ReactiveCommand<Unit,Unit> RefreshWardQueuesCommand => _reloadWard ??= ReactiveCommand.CreateFromTask(async()=>{try{await LoadWardQueuesAsync();}catch(Exception ex){LastError=ex.Message;}});
    public ReactiveCommand<Unit,Unit> UseWardQueueCommand => _useWardQueue ??= ReactiveCommand.CreateFromTask(async()=>
    {
        if(WardQueue is null || _settings is null)return;
        try {
            var credentials=await WardAccount.RequestAsync(_cdnBaseUri,$"/player/v1/rooms/{WardQueue.Id}/host",HttpMethod.Get);
            var token=credentials.GetProperty("ownerToken").GetString()!;
            _controller.Configure(_cdnBaseUri,WardQueue.Id,token);
            RoomId=WardQueue.Id;HasRoom=true;IsOwner=true;
            _settings.SqRoomId=RoomId;_settings.SqOwnerToken=token;
            SettingsSaveRequested?.Invoke(_settings);
            UpdateSubmitUrl();StartPolling();await PollOnceAsync();
        } catch(Exception ex){LastError=ex.Message;}
    });
    private async Task LoadWardQueuesAsync()
    {
        if(!WardAccount.IsConnected){StatusText="Connect Ward to load your saved queues.";return;}
        var result=await WardAccount.RequestAsync(_cdnBaseUri,"/player/v1/me/rooms",HttpMethod.Get);
        WardQueues.Clear();
        foreach(var room in result.GetProperty("rooms").EnumerateArray()){
            if(room.GetProperty("kind").GetString()!="streamer_queue")continue;
            WardQueues.Add(new(room.GetProperty("id").GetString()!,room.GetProperty("name").GetString()??"Queue",room.GetProperty("isPublic").ValueKind==System.Text.Json.JsonValueKind.True));
        }
        WardQueue=WardQueues.FirstOrDefault();
    }
}
