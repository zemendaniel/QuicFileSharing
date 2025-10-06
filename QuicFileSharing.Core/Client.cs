using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace QuicFileSharing.Core;

public class Client : QuicPeer
{
    public async Task StartAsync(IPAddress remoteAddress, int remotePort, bool isIpv6, int localPort,
        string _connToken, string expectedThumbprint, string _nickname = "Anonymous")
    {
        var clientConnectionOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(remoteAddress, remotePort),
            LocalEndPoint = new IPEndPoint(isIpv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, localPort),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol("fileShare")],
                TargetHost = "", 
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                {
                    if (certificate is X509Certificate2 cert)
                    {
                        var actualThumbprint = cert.Thumbprint; 

                        Console.WriteLine($"Server certificate thumbprint: {actualThumbprint}");

                        if (string.Equals(actualThumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase))
                        {
                            return true; // Accept
                        }
                    }
                    // Reject
                    return false;
                }
            }
        };
        nickname = _nickname;
        connToken = _connToken;
        connection = await QuicConnection.ConnectAsync(clientConnectionOptions);
        Console.WriteLine($"Connected to {connection.RemoteEndPoint}");
        
        cts = new CancellationTokenSource();
        token = cts.Token;
        
        controlStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, token);
        await controlStream.WriteAsync(new byte[] { 0x01 }, token);     // header
        SetControlStream();
        _ = Task.Run(ControlLoopAsync);
        
        fileStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, token);
        await fileStream.WriteAsync(new byte[] { 0x02 }, token); 
        SetFileStream();
        
        _ = Task.Run(PingLoopAsync, token);
        _ = Task.Run(TimeoutCheckLoopAsync, token);

        
    }
    
    public override async Task StopAsync()
    {
        if (cts != null)
            await cts.CancelAsync(); 
        
        if (connection != null)
            await connection.DisposeAsync();

        Console.WriteLine("Client stopped.");
    }
}
