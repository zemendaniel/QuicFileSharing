using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace QuicFileSharing.Core;


public class Offer
{
    public string? Ipv4 { get; init; }
    public string? Ipv6 { get; init; }
    public required int? PortV4 { get; init; }
    public required int? PortV6 { get; init; }
    public required string ClientThumbprint { get; init; }
}

public class Answer
{
    public required string Ip { get; init; }
    public required int ServerPort { get; init; }
    public required int ClientPort { get; init; }
    public required string ServerThumbprint { get; init; }
}

public class RoomInfo
{
    public required string id { get; init; } 
    public required int ex { get; init; }
}

public class SignalingMessage
{
    public required string Type { get; init; } 
    public required string Data { get; init; }
}

public class SignalingUtils
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public IPAddress? ChosenPeerIp { get; private set; }
    private IPAddress? ChosenOwnIp { get; set; }
    public int ChosenPeerPort { get; private set; }
    public int ChosenOwnPort { get; private set; }
    public bool IsIpv6 { get; private set; } 
    public string? ClientThumbprint { get; private set; }
    public string? ServerThumbprint { get; private set; }
    private StunClient stunClient;
    public int? PortV4 { get; private set; }
    public int? PortV6 { get; private set; }
    
    public SignalingUtils(string stunServerAddress = "stun.l.google.com", int stunServerPort = 19302) 
    {
        stunClient = new StunClient(stunServerAddress, stunServerPort);
    }
    
    public async Task<string> ConstructOfferAsync(string thumbprint)
    {
        //var ipv4Task = GetPublicIpv4Async();
        //var ipv6Task = GetPublicIpv6Async();
        //await Task.WhenAll(ipv4Task, ipv6Task);
        //ChosenOwnPort = GetFreeUdpPortAsync();

        var (ipv4Endpoint, ipv6Endpoint) = await RunStun();
        if (ipv4Endpoint is null && ipv6Endpoint is null)
            throw new Exception("Could not determine public IP address. Make sure you are connected to the internet.");
        PortV4 = ipv4Endpoint?.Port;
        PortV6 = ipv6Endpoint?.Port;
        var offer = new Offer
        {
            Ipv4 = ipv4Endpoint?.Address.ToString(),
            Ipv6 = ipv6Endpoint?.Address.ToString(),
            PortV4 = ipv4Endpoint?.Port,
            PortV6 = ipv6Endpoint?.Port,
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
        var peerPortV4 = offer.PortV4;
        var peerPortV6 = offer.PortV6;
        
        var (ipv4Endpoint, ipv6Endpoint) = await RunStun();
        if (ipv4Endpoint is null && ipv6Endpoint is null)
            throw new Exception("Could not determine public IP address. Make sure you are connected to the internet.");
        
        if (peerIpv6 is not null && ipv6Endpoint is not null && false) // todo
        {
            ChosenPeerIp = peerIpv6;
            ChosenOwnIp = ipv6Endpoint.Address;
            ChosenOwnPort =  ipv6Endpoint.Port;
            ChosenPeerPort = peerPortV6 ?? throw new InvalidOperationException("No port found in offer.");
            IsIpv6 = true;
            Console.WriteLine("Using IPv6");
        }
        else if (peerIpv4 is not null && ipv4Endpoint is not null)
        {
            ChosenPeerIp = peerIpv4;
            ChosenOwnIp = ipv4Endpoint.Address;
            ChosenOwnPort = ipv4Endpoint.Port;
            ChosenPeerPort = peerPortV4 ?? throw new InvalidOperationException("No port found in offer.");
            IsIpv6 = false;
            Console.WriteLine("Using IPv4");
        }
        else
            throw new InvalidOperationException("No compatible IP address found.");
        
        ClientThumbprint = offer.ClientThumbprint;
        
        var answer = new Answer
        {
            Ip = ChosenOwnIp.ToString(),
            ServerPort = ChosenOwnPort,
            ClientPort = ChosenPeerPort,
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
        ChosenPeerPort = answer.ServerPort;
        ChosenOwnPort = answer.ClientPort;
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
            var response = await httpClient.GetStringAsync("https://ipv6.seeip.org/");
            if (IPAddress.TryParse(response.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
                return ip;
        }
        catch
        {
            // Ignore
        }
        return null;
    }
    private async Task<(IPEndPoint?, IPEndPoint?)> RunStun()
    {
        return await stunClient.RunAsync();
    }

    private static async Task PunchUdpHoleAsync(IPAddress peerIp, int peerPort, int localPort)
    {
        var localEndpoint = peerIp.AddressFamily == AddressFamily.InterNetworkV6 ? new IPEndPoint(IPAddress.IPv6Any, localPort) : new IPEndPoint(IPAddress.Any, localPort);
        var remoteEndpoint = new IPEndPoint(peerIp, peerPort);
        Console.WriteLine("Sending from " + localEndpoint + " to " + remoteEndpoint);
        using var udp = new UdpClient(localEndpoint);
        
        List<Task> tasks = [];
        for (var i = 0; i < 5; i++)
            tasks.Add(udp.SendAsync([1], 1, remoteEndpoint));
        
        await Task.WhenAll(tasks);
    }
}