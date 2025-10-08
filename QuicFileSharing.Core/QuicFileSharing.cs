using System.Text.Json;

namespace QuicFileSharing.Core;

public class QuicFileSharing
{
    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public static async Task Start(Role role, string wsBaseUri, string roomId = "")
    {
        WebSocketSignaling signaling;
        QuicPeer peer;
        bool success;
        SignalingMessage? msg;
        var utils = new SignalingUtils();
        switch (role)
        {
            case Role.Server:
                signaling = new WebSocketSignaling(wsBaseUri, Role.Server);
                peer = new Server();
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
                            Console.WriteLine(info.id);
                            break;
                        case "offer":
                            var answer = await utils.ConstructAnswerAsync(msg.Data, server!.Thumbprint);
                            await server.StartAsync(utils.IsIpv6, utils.ChosenOwnPort, utils.ClientThumbprint);
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
                
                // peer.InitSend("/home/zemen/a.txt");
                // await peer.StartSending();
                await Task.Delay(-1);
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
                            break;
                    }
                };
                signaling.OnDisconnected += (reason, description) =>
                {
                    Console.WriteLine($"Disconnected from signaling server. Reason: {reason}, Description: {description}");
                };

                var offer = await utils.ConstructOfferAsync(client.Thumbprint);
                await signaling.SendAsync(offer, "offer");
                // client.InitReceive("/home/zemen/test");
                await Task.Delay(-1);
                break;
        }
    }
}