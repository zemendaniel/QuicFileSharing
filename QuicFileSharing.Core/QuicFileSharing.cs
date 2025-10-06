using System;
using System.Threading.Tasks;

namespace QuicFileSharing.Core;

class QuicFileSharing
{
    public static async Task Start(Role role, string wsBaseUri, string roomId = "")
    {
        WebSocketSignaling signaling;
        switch (role)
        {
            case Role.Server:
                signaling = new WebSocketSignaling(wsBaseUri, Role.Server);
                signaling.OnMessageReceived += message =>
                {
                    // todo
                    Console.WriteLine("Received message from signaling server:");
                    Console.WriteLine(message);
                };
                signaling.OnDisconnected += (reason, description) =>
                {
                    Console.WriteLine($"Disconnected from signaling server. Reason: {reason}, Description: {description}");
                };
                await signaling.ConnectAsync();

                
                var server = new Server();
                // await server.StartAsync(5000); todo
                server.InitSend("/home/zemen/a.txt");
                await server.StartSending();
                await Task.Delay(-1);
                break;

            case Role.Client:
                signaling = new WebSocketSignaling(wsBaseUri, Role.Client);
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