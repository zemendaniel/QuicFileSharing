using System.Net.WebSockets;
using System.Text;

namespace QuicFileSharing.Core;

enum Role
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

    public async Task ConnectAsync(string? roomId = null)
    {
        if (ws is not { State: WebSocketState.None })
            throw new InvalidOperationException("WebSocket already connected or disposed");
        
        var uriBuilder = new StringBuilder($"{baseUri}/ws/rooms?role={role.ToString().ToLower()}");

        if (role == Role.Client)
        {
            if (string.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("Client must provide room id");
            uriBuilder.Append($"&room_id={roomId}");
        }

        var uri = new Uri(uriBuilder.ToString());
        try
        {
            await ws.ConnectAsync(uri, cts.Token);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        receiveTask = Task.Run(ReceiveAsync, cts.Token);
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

    public async Task SendAsync(string message)
    {
        if (ws is not { State: WebSocketState.Open })
            throw new InvalidOperationException("WebSocket not connected");

        var bytes = Encoding.UTF8.GetBytes(message);
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

