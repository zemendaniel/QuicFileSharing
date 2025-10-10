using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace QuicFileSharing.Core;

public enum Role
{
    Server,
    Client
}

public class WebSocketSignaling: IDisposable
{
    private readonly string baseUri;
    private readonly CancellationTokenSource cts = new();
    private readonly ClientWebSocket ws;
    public event Action<string>? OnMessageReceived;
    public event Action<string?, string?>? OnDisconnected;
    
    public WebSocketSignaling(string baseUri)
    {
        this.baseUri = baseUri;
        ws = new ClientWebSocket();
    }

    public async Task<(bool Success, string? ErrorMessage)> ConnectAsync(Role role, string? roomId = null)
    {
        if (ws is not { State: WebSocketState.None })
            return (false, "WebSocket already connected");
    
        var uriBuilder = new StringBuilder($"{baseUri}/ws/rooms?role={role.ToString().ToLower()}");

        if (role == Role.Client)
        {
            if (string.IsNullOrWhiteSpace(roomId))
                return (false, "Client must provide a room code");
            uriBuilder.Append($"&room_id={roomId}");
        }

        var uri = new Uri(uriBuilder.ToString());
        try
        {
            await ws.ConnectAsync(uri, cts.Token);
            _ = Task.Run(ReceiveAsync, cts.Token);
            return (true, null);
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            Console.WriteLine("WebSocket closed prematurely: " + ex.Message);
            return (false, ex.Message);
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"WebSocket error: {ex.WebSocketErrorCode} - {ex.Message}");
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected exception while connecting: {ex}");
            return (false, ex.Message);
        }
    }

    private async Task ReceiveAsync()
    {
        if (ws is not { State: WebSocketState.Open })
            throw new InvalidOperationException("WebSocket not connected");
        
        var buffer = new byte[4096];

        while (!cts.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            try
            {
                var result = await ws.ReceiveAsync(buffer, cts.Token);
                Console.WriteLine($"Received frame: type={result.MessageType}, count={result.Count}");

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await HandleDisconnect(ws.CloseStatus, ws.CloseStatusDescription);
                    return;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                HandleMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Receive error: {ex.Message}");
                await HandleDisconnect(ws.CloseStatus, ws.CloseStatusDescription);
                return;
            }
        }
    }
    
    private async Task HandleDisconnect(WebSocketCloseStatus? status, string? reason)
    {
        OnDisconnected?.Invoke(status.ToString(), reason);
        await CloseAsync();
    }
    
    private void HandleMessage(string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            OnMessageReceived?.Invoke(message);
        }
        catch
        {
            // ignored
        }
    }

    public async Task SendAsync(string message, string type)
    {
        if (ws is not { State: WebSocketState.Open })
            throw new InvalidOperationException("WebSocket not connected");

        var msg = new SignalingMessage
        {
            Type = type,
            Data = message
        };
        var json = JsonSerializer.Serialize(msg);

        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
        Console.WriteLine($"[OUT] {message}");
    }

    public async Task CloseAsync()
    {
        try
        {
            await cts.CancelAsync();
            if (ws is { State: WebSocketState.Open })
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch
        {
            // ignored
        }
    }

    public void Dispose()
    {
        CloseAsync(); // todo
    }
}

