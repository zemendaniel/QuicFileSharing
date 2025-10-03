using System;
using System.Threading.Tasks;

namespace QuicFileSharing.Core;

class QuicFileSharing
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run [server|client]");
            return;
        }

        switch (args[0].ToLower())
        {
            case "server":
                var serverSignaling = new WebSocketSignaling("ws://vps.zemendaniel.hu:8080", Role.Server);
                serverSignaling.OnMessageReceived += message =>
                {
                    Console.WriteLine("Received message from signaling server:");
                    Console.WriteLine(message);
                };
                serverSignaling.OnDisconnected += (reason, description) =>
                {
                    Console.WriteLine($"Disconnected from signaling server. Reason: {reason}, Description: {description}");
                };
                await serverSignaling.ConnectAsync();

                
                var server = new Server();
                await server.StartAsync(5000);
                server.InitSend("/home/zemen/a.txt");
                await server.StartSending();
                await Task.Delay(-1);
                break;

            case "client":
                var clientSignaling = new WebSocketSignaling("ws://vps.zemendaniel.hu:8080", Role.Client);
                Console.WriteLine("Room ID:");
                var roomId = Console.ReadLine()!.Trim();
                await clientSignaling.ConnectAsync(roomId);
                var client = new Client();
                await client.StartAsync();
                client.InitReceive("/home/zemen/test");
                await Task.Delay(-1);
                break;

            default:
                Console.WriteLine("Unknown argument. Use 'server' or 'client'.");
                break;
        }
    }
}