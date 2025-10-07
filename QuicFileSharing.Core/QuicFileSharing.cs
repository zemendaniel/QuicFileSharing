using System.Text.Json;

namespace QuicFileSharing.Core;

class QuicFileSharing
{
    public static async Task Start(Role role, string wsBaseUri, string nickname = "Anonymous", string roomId = "")
    {
        WebSocketSignaling signaling;
        QuicPeer peer;
        switch (role)
        {
            case Role.Server:
                signaling = new WebSocketSignaling(wsBaseUri, Role.Server);
                await signaling.ConnectAsync();
                peer = new Server();
                var server = peer as Server;
                var utils = new SignalingUtils();
                signaling.OnMessageReceived += async message =>
                {
                    Console.WriteLine(message);
                    var msg = JsonSerializer.Deserialize<SignalingMessage>(message);
                    if (msg == null) return;
                    switch (msg.Type)
                    {
                        case "room_info":
                            var info = JsonSerializer.Deserialize<RoomInfo>(msg.Data);
                            if (info == null) return;
                            Console.WriteLine(info.RoomId);
                            break;
                        case "offer":
                            var answer = await utils.ConstructAnswerAsync(msg.Data, server!.Thumbprint,
                                server.ConnToken, nickname);
                            await signaling.SendAsync(answer);
                            await server.StartAsync(utils.IsIpv6, utils.ChosenOwnPort, nickname);
                            break;
                    }
                };
                signaling.OnDisconnected += (reason, description) =>
                {
                    Console.WriteLine($"Disconnected from signaling server. Reason: {reason}, Description: {description}");
                };
                
                peer.InitSend("/home/zemen/a.txt");
                await peer.StartSending();
                await Task.Delay(-1);
                break;

            case Role.Client:
                signaling = new WebSocketSignaling(wsBaseUri, Role.Client);
                if (string.IsNullOrWhiteSpace(roomId))
                    throw new ArgumentException("Client must provide room id");
                await signaling.ConnectAsync(roomId);
                Console.WriteLine("Room ID:");
                //var roomId = Console.ReadLine()!.Trim();
                await signaling.ConnectAsync(roomId);
                var client = new Client();
                // await client.StartAsync(); todo
                client.InitReceive("/home/zemen/test");
                await Task.Delay(-1);
                break;
        }
    }
}