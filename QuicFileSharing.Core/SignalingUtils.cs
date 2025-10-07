using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace QuicFileSharing.Core;


class Offer
{
    public string? Ipv4 { get; init; }
    public string? Ipv6 { get; init; }
    public required int Port { get; init; }
    public required string ClientThumbprint { get; init; }
}

class Answer
{
    public required string Ip { get; init; }
    public required int Port { get; init; }
    public required string ServerThumbprint { get; init; }
}

class RoomInfo
{
    public required string id { get; init; } 
    public required int ex { get; init; }
}

class SignalingMessage
{
    public required string Type { get; init; } 
    public required string Data { get; init; }
}

class SignalingUtils
{
    public IPAddress? ChosenPeerIp { get; private set; }
    private IPAddress? ChosenOwnIp { get; set; }
    public int ChosenPeerPort { get; private set; }
    public int ChosenOwnPort { get; private set; }
    public bool IsIpv6 { get; private set; } 
    public string? ClientThumbprint { get; private set; }
    public string? ServerThumbprint { get; private set; }
    public async Task<string> ConstructOfferAsync(string thumbprint)
    {
        var ipv4Task = GetPublicIpv4Async();
        var ipv6Task = GetPublicIpv6Async();
        await Task.WhenAll(ipv4Task, ipv6Task);
        ChosenOwnPort = GetFreeUdpPortAsync();
        var offer = new Offer
        {
            Ipv4 = ipv4Task.Result?.ToString(),
            Ipv6 = ipv6Task.Result?.ToString(),
            Port = ChosenOwnPort,
            ClientThumbprint = thumbprint
        };
        var json = JsonSerializer.Serialize(offer);
        Console.WriteLine(json);
        return json;
    }
    public async Task<string> ConstructAnswerAsync(string offerJson, string thumbprint)
    {
        var offer = JsonSerializer.Deserialize<Offer>(offerJson) ?? throw new ArgumentException("Invalid offer JSON");
        
        var peerIpv6 = string.IsNullOrWhiteSpace(offer.Ipv6) ? null : IPAddress.Parse(offer.Ipv6);
        var peerIpv4 = string.IsNullOrWhiteSpace(offer.Ipv4) ? null : IPAddress.Parse(offer.Ipv4);

        var ipv4Task = GetPublicIpv4Async();
        var ipv6Task = GetPublicIpv6Async();
        await Task.WhenAll(ipv4Task, ipv6Task);
        var ipv4 = ipv4Task.Result;
        var ipv6 = ipv6Task.Result;
        
        if (peerIpv6 is not null && ipv6 is not null)
        {
            ChosenPeerIp = peerIpv6;
            ChosenOwnIp = ipv6;
            IsIpv6 = true;
            Console.WriteLine("Using IPv6");
        }
        else if (peerIpv4 is not null && ipv4 is not null)
        {
            ChosenPeerIp = peerIpv4;
            ChosenOwnIp = ipv4;
            Console.WriteLine("Using IPv4");
        }
        else
            throw new InvalidOperationException("No compatible IP address found.");
        
        ChosenOwnPort = GetFreeUdpPortAsync();
        ChosenPeerPort = offer.Port;
        ClientThumbprint = offer.ClientThumbprint;
        
        var answer = new Answer
        {
            Ip = ChosenOwnIp.ToString(),
            Port = ChosenOwnPort,
            ServerThumbprint = thumbprint,
        };
        var json = JsonSerializer.Serialize(answer);
        
        await PunchUdpHoleAsync(ChosenPeerIp, ChosenPeerPort, ChosenOwnPort);
        
        Console.WriteLine(json);
        return json;
    }
    public void ProcessAnswer(string answerJson)
    {
        var answer = JsonSerializer.Deserialize<Answer>(answerJson) ?? throw new ArgumentException("Invalid answer JSON");

        ChosenPeerIp = IPAddress.Parse(answer.Ip);
        ChosenPeerPort = answer.Port;
        ServerThumbprint = answer.ServerThumbprint;
        IsIpv6 = ChosenPeerIp.AddressFamily == AddressFamily.InterNetworkV6;
        Console.WriteLine($"Chosen peer IP: {ChosenPeerIp}");
    }
    private static int GetFreeUdpPortAsync()
    {
        // Race condition is very unlikely in this context as the OS usually cycles ports
        using var udp = new UdpClient(0);
        return udp.Client.LocalEndPoint is IPEndPoint ep ? ep.Port : throw new InvalidOperationException("Failed to get free UDP port");
    }
    private static async Task<IPAddress?> GetPublicIpv6Async()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            var response = await httpClient.GetStringAsync("https://api64.ipify.org");
            if (IPAddress.TryParse(response.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
                return ip;
        }
        catch
        {
            // Ignore
        }
        return null;
    }
    private static async Task<IPAddress?> GetPublicIpv4Async()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            var response = await httpClient.GetStringAsync("https://api.ipify.org");
            if (IPAddress.TryParse(response.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                return ip;
        }
        catch
        {
            // Ignore
        }
        return null;
    }

    private static async Task PunchUdpHoleAsync(IPAddress peerIp, int peerPort, int localPort)
    {
        var localEndpoint = peerIp.AddressFamily == AddressFamily.InterNetworkV6 ? new IPEndPoint(IPAddress.IPv6Any, localPort) : new IPEndPoint(IPAddress.Any, localPort);
        var remoteEndpoint = new IPEndPoint(peerIp, peerPort);
        using var udp = new UdpClient(localEndpoint);
        
        List<Task> tasks = [];
        for (var i = 0; i < 5; i++)
            tasks.Add(udp.SendAsync([], 0, remoteEndpoint));
        
        await Task.WhenAll(tasks);
    }
}