using System.Net;
using System.Net.Sockets;

namespace QuicFileSharing.Core;


class Offer
{
    public IPAddress? Ipv4 { get; init; }
    public IPAddress? Ipv6 { get; init; }
    public required int Port { get; init; }
    public string Nickname { get; init; } = "Anonymous";
}

class Answer
{
    public required IPAddress Ip { get; init; }
    public required int Port { get; init; }
    public string Nickname { get; init; } = "Anonymous";
    public required string Token { get; init; }
    public required string Thumbprint { get; init; }
}

class RoomInfo
{
    public required string RoomId { get; init; } 
    public required int ExpirySeconds { get; init; }
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
    public static async Task<string> ConstructOfferAsync(string nickname)
    {
        var ipv4Task = GetPublicIpv4Async();
        var ipv6Task = GetPublicIpv6Async();
        await Task.WhenAll(ipv4Task, ipv6Task);
        var offer = new Offer
        {
            Ipv4 = ipv4Task.Result,
            Ipv6 = ipv6Task.Result,
            Nickname = string.IsNullOrWhiteSpace(nickname) ? "Anonymous" : nickname,
            Port = GetFreeUdpPortAsync()
        };
        var json = System.Text.Json.JsonSerializer.Serialize(offer);
        Console.WriteLine(json);
        return json;
    }
    public async Task<string> ConstructAnswerAsync(string offerJson, string thumbprint, string token, string nickname)
    {
        var offer = System.Text.Json.JsonSerializer.Deserialize<Offer>(offerJson);
        if (offer == null)
            throw new ArgumentException("Invalid offer JSON");

        var ipv4Task = GetPublicIpv4Async();
        var ipv6Task = GetPublicIpv6Async();
        await Task.WhenAll(ipv4Task, ipv6Task);
        var ipv4 = ipv4Task.Result;
        var ipv6 = ipv6Task.Result;
        
        if (offer.Ipv6 is not null && ipv6 is not null)
        {
            ChosenPeerIp = offer.Ipv6;
            ChosenOwnIp = ipv6;
            IsIpv6 = true;
            Console.WriteLine("Using IPv6");
        }
        else if (offer.Ipv4 is not null && ipv4 is not null)
        {
            ChosenPeerIp = offer.Ipv4;
            ChosenOwnIp = ipv4;
            Console.WriteLine("Using IPv4");
        }
        else
            throw new InvalidOperationException("No compatible IP address found.");
        
        ChosenOwnPort = GetFreeUdpPortAsync();
        
        var answer = new Answer
        {
            Ip = ChosenOwnIp,
            Port = ChosenOwnPort,
            Thumbprint = thumbprint,
            Token = token,
            Nickname = string.IsNullOrWhiteSpace(nickname) ? "Anonymous" : nickname
        };
        var json = System.Text.Json.JsonSerializer.Serialize(answer);
        
        await PunchUdpHoleAsync(ChosenPeerIp, ChosenPeerPort, ChosenOwnPort);
        
        Console.WriteLine(json);
        return json;
    }
    public void ProcessAnswer(string answerJson)
    {
        var answer = System.Text.Json.JsonSerializer.Deserialize<Answer>(answerJson);
        if (answer == null)
            throw new ArgumentException("Invalid answer JSON");
        ChosenPeerIp = answer.Ip;
        ChosenPeerPort = answer.Port;
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