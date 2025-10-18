using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace QuicFileSharing.Core;


public class Offer
{
    public string? ClientIpv4 { get; init; }
    public string? ClientIpv6 { get; init; }
    public int? ClientPortV6 { get; init; }
    public required string ClientThumbprint { get; init; }
}

public class Answer
{
    public required string ServerIp { get; init; }
    public required int ServerPort { get; init; }
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

public class SignalingUtils: IDisposable
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public IPAddress? PeerIp { get; private set; }
    private IPAddress? OwnIp { get; set; }
    public int PeerPort { get; private set; }
    public int? OwnPort { get; private set; }  
    public bool ForceIpv4 { get; private set; } 
    public string? ClientThumbprint { get; private set; }
    public string? ServerThumbprint { get; private set; }
    private UdpClient? portKeepAliveSocket;
    
    
    public async Task<string> ConstructOfferAsync(string thumbprint)
    {
        var ipv4Task = GetPublicIpv4Async();
        var ipv6Task = GetPublicIpv6Async();
        
        await Task.WhenAll(ipv4Task, ipv6Task);
        
        var (ipv4, ipv6) = (ipv4Task.Result, ipv6Task.Result);
        if (ipv6 is not null)
            OwnPort = GetFreeUdpPort();
        
        var offer = new Offer
        {
            ClientIpv4 = ipv4?.ToString(),
            ClientIpv6 = ipv6?.ToString(),
            ClientPortV6 = OwnPort ?? null,
            ClientThumbprint = thumbprint
        };
        var json = JsonSerializer.Serialize(offer);
        return json;
    }
    public async Task<string> ConstructAnswerAsync(string offerJson, string serverThumbprint, bool forceIpv4 = false, int? port = null)
    {
        var offer = JsonSerializer.Deserialize<Offer>(offerJson) ?? throw new ArgumentException("Invalid offer JSON");
        
        var clientIpv4 = string.IsNullOrWhiteSpace(offer.ClientIpv4) ? null : IPAddress.Parse(offer.ClientIpv4);
        var clientIpv6 = string.IsNullOrWhiteSpace(offer.ClientIpv6) || forceIpv4 ? null : IPAddress.Parse(offer.ClientIpv6!);
        
        IPAddress? serverIp;
        if (forceIpv4)
            serverIp = await GetPublicIpv4Async();
        else
            serverIp = await GetPublicIpv6Async();
        
        if (serverIp is null && !forceIpv4)
            throw new InvalidOperationException("Failed to get public IPv6 address. Are you connected to the internet?" +
                                                "If you do not have IPv6 connectivity, you can configure using IPv4 in the settings.");
        
        if (serverIp is null && forceIpv4)
            throw new InvalidOperationException("Failed to get public IPv4 address. Are you connected to the internet?");
        
        if (serverIp is null)
            throw new InvalidOperationException("Failed to get public IP address. Are you connected to the internet?");
        
        if (clientIpv4 is null && clientIpv6 is null)
            throw new InvalidOperationException("Peer did not provide IP address.");
        
        if (clientIpv6 is not null && serverIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            PeerIp = clientIpv6;
            PeerPort = offer.ClientPortV6 ?? throw new InvalidOperationException("No port specified in offer.");
            OwnPort = GetFreeUdpPort();
            Console.WriteLine("Using IPv6");
        }
        else if (clientIpv4 is not null && serverIp.AddressFamily == AddressFamily.InterNetwork)
        {
            Console.WriteLine("Using IPv4");
        }
        else
            throw new InvalidOperationException("No compatible IP address found. Does your peer have IPv6 connectivity?" +
                                                "If not, you can configure using IPv4 in the settings.");
        
        OwnIp = serverIp;
        ClientThumbprint = offer.ClientThumbprint;
        
        var answer = new Answer
        {
            ServerIp = OwnIp.ToString(),
            ServerPort = OwnPort ?? port ?? throw new InvalidOperationException("No port specified in offer."),
            ServerThumbprint = serverThumbprint,
        };
        var json = JsonSerializer.Serialize(answer);
        
        if (serverIp.AddressFamily == AddressFamily.InterNetworkV6)
            await PunchUdpHoleAsync(PeerIp!, PeerPort);
        
        return json;
    }
    public void ProcessAnswer(string answerJson)
    {
        var answer = JsonSerializer.Deserialize<Answer>(answerJson) ?? throw new ArgumentException("Invalid answer JSON");

        PeerIp = IPAddress.Parse(answer.ServerIp);
        PeerPort = answer.ServerPort;
        ServerThumbprint = answer.ServerThumbprint; ;
    }
    private int GetFreeUdpPort()
    {
        // Race condition is very unlikely in this context as the OS usually cycles ports
        var localEndpoint = new IPEndPoint(IPAddress.IPv6Any, 0);
        portKeepAliveSocket = new(localEndpoint);
        return portKeepAliveSocket.Client.LocalEndPoint is IPEndPoint ep ? ep.Port : throw new InvalidOperationException("Failed to get free UDP port");
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

    private async Task PunchUdpHoleAsync(IPAddress peerIp, int peerPort)
    {
        var remoteEndpoint = new IPEndPoint(peerIp, peerPort);
        if (portKeepAliveSocket is null)
            throw new InvalidOperationException("No port keep alive socket.");
        
        List<Task> tasks = [];
        for (var i = 0; i < 5; i++)
            tasks.Add(portKeepAliveSocket.SendAsync([1], 1, remoteEndpoint));
        
        await Task.WhenAll(tasks);
    }
    public void CloseUdpSocket()
    {
        portKeepAliveSocket?.Close();
    }

    public void Dispose()
    {
        CloseUdpSocket();
    }
}