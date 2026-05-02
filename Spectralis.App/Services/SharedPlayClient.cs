using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.SharedPlay;

namespace Spectralis.App.Services
{
    public class SharedPlayClient : IDisposable
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private readonly string _serverUri;

        public event EventHandler<SharedPlaySyncPacket>? SyncReceived;
        public event EventHandler<string>? SessionEnded;
        public event EventHandler? Disconnected;

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public string? SessionId { get; private set; }

        public SharedPlayClient(string serverUri)
        {
            _serverUri = serverUri;
        }

        public async Task ConnectAsync(string roomCode, string userId)
        {
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            await _ws.ConnectAsync(new Uri($"{_serverUri}/ws/{roomCode}?user={userId}"), _cts.Token);
            _ = ReceiveLoopAsync(_cts.Token);
        }

        public async Task SendSyncAsync(SharedPlaySyncPacket packet)
        {
            if (_ws?.State != WebSocketState.Open) return;
            string json = JsonSerializer.Serialize(packet);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buf = new byte[8192];
            try
            {
                while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await _ws.ReceiveAsync(buf, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Disconnected?.Invoke(this, EventArgs.Empty);
                        break;
                    }
                    string json = Encoding.UTF8.GetString(buf, 0, result.Count);
                    var packet = JsonSerializer.Deserialize<SharedPlaySyncPacket>(json);
                    if (packet != null)
                    {
                        if (packet.Event == "ended")
                            SessionEnded?.Invoke(this, packet.SessionId);
                        else
                            SyncReceived?.Invoke(this, packet);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { Disconnected?.Invoke(this, EventArgs.Empty); }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _ws?.Dispose();
            _cts?.Dispose();
        }
    }
}
