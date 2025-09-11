using System;
using System.Threading.Tasks;

class QuicFileSharing
{
    public static int fileChunkSize = 16 * 1024 * 1024;
    public static int messageChunkSize = 1024;
    public static int fileBufferSize = 1014 * 1024;
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run [server|client]");
            return;
        }

        switch (args[0].ToLower())
        {
            case "server":
                var server = new Server();
                await server.StartAsync(5000);
                server.InitReceive("/root/file-test");
                await server.StartReceiving();
                await Task.Delay(-1);
                break;

            case "client":
                var client = new Client();
                await client.StartAsync();
                client.InitSend("/root/test.txt");
                await client.StartSending();
                await Task.Delay(-1);
                break;

            default:
                Console.WriteLine("Unknown argument. Use 'server' or 'client'.");
                break;
        }
    }
}