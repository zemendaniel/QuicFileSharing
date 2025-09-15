using System.Net;
using System.Net.Quic;
using System.Net.Security;

namespace QuicFileSharing.Core;

public class Client : QuicPeer
{
    public override async Task StartAsync(int port = 5000)
    {
        var clientConnectionOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, port),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol("fileShare")],
                TargetHost = "", 
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            }
        };
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
