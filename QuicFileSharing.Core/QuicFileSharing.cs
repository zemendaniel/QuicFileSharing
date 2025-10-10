using System.Text.Json;

namespace QuicFileSharing.Core;

public class QuicFileSharing
{
    private QuicPeer peer;
    public string? RoomId { get; private set; }
    
    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public event Action<string>? RoomCreated;
    public event Action<string>? RoomJoined;
    public event Action? ClientConnected;
    public event Action? ClientDisconnected;
    public event Action? ConnectedToServer;
    public event Action? ConnectionReady;
    public event Action<string?>? OnDisconnected;
    
    private void AttachPeerEvents()
    {
        peer.ConnectionReady += () => ConnectionReady?.Invoke();
    }

    public async Task<(bool, string?)> Start(Role role, string wsBaseUri, string roomId = "")
    {
        WebSocketSignaling signaling;
        bool? success;
        string? errorMessage;
        SignalingMessage? msg;
        var utils = new SignalingUtils();
        
        switch (role)
        {
            case Role.Server:
                signaling = new WebSocketSignaling(wsBaseUri, Role.Server);
                peer = new Server();
                AttachPeerEvents();
                
                var server = peer as Server;
                signaling.OnDisconnected += (reason, description) =>
                {
                    Console.WriteLine($"Disconnected from signaling server. Reason: {reason}, Description: {description}");
                };
                signaling.OnMessageReceived += async message =>
                {
                    Console.WriteLine(message);
                    msg = JsonSerializer.Deserialize<SignalingMessage>(message, options);
                    if (msg == null) return;
                    switch (msg.Type)
                    {
                        case "room_info":
                            var info = JsonSerializer.Deserialize<RoomInfo>(msg.Data);
                            if (info is null) return;
                            RoomCreated?.Invoke(info.id);
                            break;
                        case "offer":
                            var answer = await utils.ConstructAnswerAsync(msg.Data, server!.Thumbprint);
                            _ = Task.Run(() => server.StartAsync(utils.IsIpv6, utils.ChosenOwnPort, utils.ClientThumbprint));
                            await signaling.SendAsync(answer, "answer");
                            break;
                    }
                };
                (success, errorMessage) = await signaling.ConnectAsync();
                if (success is not true)
                {
                    Console.WriteLine($"Failed to connect to signaling server: {errorMessage}");
                    return (false, errorMessage);
                }
                
                break;

            case Role.Client:
                signaling = new WebSocketSignaling(wsBaseUri, Role.Client);
                peer = new Client();
                var client = (peer as Client)!;
                AttachPeerEvents();
                if (string.IsNullOrWhiteSpace(roomId))
                    return (false, "Client must provide a room code");
                
                signaling.OnDisconnected += (reason, description) =>
                {
                    OnDisconnected?.Invoke(description);
                    throw new Exception(description);
                };
                signaling.OnMessageReceived += async message =>
                {
                    Console.WriteLine(message);
                    msg = JsonSerializer.Deserialize<SignalingMessage>(message, options);                    
                    if (msg == null) return;
                    switch (msg.Type)
                    {
                        case "answer":
                            utils.ProcessAnswer(msg.Data);
                            await signaling.CloseAsync();
                            await client.StartAsync(utils.ChosenPeerIp, utils.ChosenPeerPort, utils.IsIpv6,
                                utils.ChosenOwnPort, utils.ServerThumbprint);
                            ConnectedToServer?.Invoke();
                            break;
                    }
                };
                (success, errorMessage) = await signaling.ConnectAsync(roomId);
                if (success is not true)
                {
                    Console.WriteLine("Failed to connect to signaling server.");
                    return (false, errorMessage);
                }
                
                
                var offer = await utils.ConstructOfferAsync(client.Thumbprint);
                
                (success, errorMessage) = await signaling.SendAsync(offer, "offer");
                if (success is not true)
                {
                    Console.WriteLine($"Failed to send offer to signaling server: {errorMessage}");
                    return (false, errorMessage);
                }
                break;
        }
        return (true, null);
    }
}