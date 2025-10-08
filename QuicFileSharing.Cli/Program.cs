// This is a simple console app to test the library
using QuicFileSharing.Core;
using System;

namespace QuicFileSharing.Cli;

class Program
{
    static async Task Main(string[] args)
    {
        var roleInp = Console.ReadLine();
        var roomId = Console.ReadLine() ?? "";

        Role r;
        switch (roleInp)
        {
            case "s":
                r = Role.Server;
                break;
            case "c":
                r = Role.Client;
                break;
            default:
                throw new ArgumentException("Invalid role.");
        }

        //await QuicFileSharing.Core.QuicFileSharing.Start(r, "ws://vps.zemendaniel.hu:8080", roomId);
    }
}