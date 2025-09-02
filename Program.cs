using System;
using System.Threading.Tasks;

class Program
{
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
                break;

            case "client":
                var client = new Client();
                await client.ConnectAndSendMessageAsync();
                break;

            default:
                Console.WriteLine("Unknown argument. Use 'server' or 'client'.");
                break;
        }
    }
}