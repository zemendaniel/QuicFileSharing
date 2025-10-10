using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace QuicFileSharing.Core;

public enum Role
{
    Server,
    Client
}

class WebSocketSignaling : IDisposable
{
    private readonly string baseUri;
    private readonly Role role;
    private readonly CancellationTokenSource cts = new();
    private readonly ClientWebSocket ws;
    private bool disposed;
    private Task? receiveTask;
    public event Action<string>? OnMessageReceived;
    public event Action<string?, string?>? OnDisconnected;
    
    public WebSocketSignaling(string baseUri, Role role)
    {
        this.baseUri = baseUri;
        this.role = role;
        ws = new ClientWebSocket();
    }

    public async Task<(bool Success, string? ErrorMessage)> ConnectAsync(string? roomId = null)
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
            receiveTask = Task.Run(ReceiveAsync, cts.Token);
            return (true, null); // connected successfully
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
                    HandleDisconnect(ws.CloseStatus, ws.CloseStatusDescription);
                    return;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                HandleMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Receive error: {ex.Message}");
                HandleDisconnect(ws.CloseStatus, ws.CloseStatusDescription);
                return;
            }
        }
    }
    
    private void HandleDisconnect(WebSocketCloseStatus? status, string? reason)
    {
        if (disposed) return;
        OnDisconnected?.Invoke(status.ToString(), reason);
        Dispose();
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

    public async Task<(bool success, string? errorMessage)>SendAsync(string message, string type)
    {
        if (ws is not { State: WebSocketState.Open })
            return (false, "WebSocket not connected");

        var msg = new SignalingMessage
        {
            Type = type,
            Data = message
        };
        var json = JsonSerializer.Serialize(msg);

        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
        Console.WriteLine($"[OUT] {message}");
        return (true, null);
    }

    public async Task CloseAsync()
    {
        try
        {
            await cts.CancelAsync();
            if (ws is { State: WebSocketState.Open })
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            
            if (receiveTask is { IsCompleted: false })
                await receiveTask;
        }
        catch
        {
            // ignored
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cts.Dispose();
        ws.Dispose();
    }
}

