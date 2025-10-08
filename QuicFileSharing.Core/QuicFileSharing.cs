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
    
    private void AttachPeerEvents()
    {
        if (peer == null) return;

        peer.ConnectionReady += () => ConnectionReady?.Invoke();
    }
    
    public async Task Start(Role role, string wsBaseUri, string roomId = "")
    {
        WebSocketSignaling signaling;
        bool success;
        SignalingMessage? msg;
        var utils = new SignalingUtils();
        
        switch (role)
        {
            case Role.Server:
                signaling = new WebSocketSignaling(wsBaseUri, Role.Server);
                peer = new Server();
                AttachPeerEvents();
                
                var server = peer as Server;
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
                success = await signaling.ConnectAsync();
                if (!success)
                {
                    Console.WriteLine("Failed to connect to signaling server.");
                    return;
                }
                signaling.OnDisconnected += (reason, description) =>
                {
                    Console.WriteLine($"Disconnected from signaling server. Reason: {reason}, Description: {description}");
                };
                
                break;

            case Role.Client:
                signaling = new WebSocketSignaling(wsBaseUri, Role.Client);
                if (string.IsNullOrWhiteSpace(roomId))
                    throw new ArgumentException("Client must provide room id");
                success = await signaling.ConnectAsync(roomId);
                if (!success)
                {
                    Console.WriteLine("Failed to connect to signaling server.");
                    return;
                }
                peer = new Client();
                var client = (peer as Client)!;
                AttachPeerEvents();
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
                signaling.OnDisconnected += (reason, description) =>
                {
                    Console.WriteLine($"Disconnected from signaling server. Reason: {reason}, Description: {description}");
                };

                var offer = await utils.ConstructOfferAsync(client.Thumbprint);
                await signaling.SendAsync(offer, "offer");

                break;
        }
    }
}