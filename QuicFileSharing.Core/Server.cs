using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace QuicFileSharing.Core;

public class Server: QuicPeer
{
    private QuicListener? listener;
    public event Action? ClientConnected;
    public event Action? ClientDisconnected;
    
    public async Task StartAsync(bool isIpv6, int localPort, string expectedThumbprint)
    {
        var listenEndpoint = new IPEndPoint(isIpv6 ? IPAddress.IPv6Any : IPAddress.Any, localPort);
        var serverConnectionOptions = new QuicServerConnectionOptions
        {
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            ServerAuthenticationOptions = new SslServerAuthenticationOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol("fileShare")],
                ServerCertificate = cert,
                ClientCertificateRequired = true,
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                {
                    if (certificate is X509Certificate2 clientCert)
                    {
                        Console.WriteLine("Server expected print: " + expectedThumbprint);
                        Console.WriteLine("Server thumbprint: " + clientCert.Thumbprint);
                        return clientCert.Thumbprint.Equals(expectedThumbprint, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                }
            }
        };

        listener = await QuicListener.ListenAsync(new QuicListenerOptions
        {
            ListenEndPoint = listenEndpoint,
            ApplicationProtocols = [new SslApplicationProtocol("fileShare")],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(serverConnectionOptions)
        });
        Console.WriteLine($"Server listening on {listenEndpoint.Address}:{localPort}");
        
        cts = new CancellationTokenSource();
        token = cts.Token;
        
        var connTask = Task.Run(AcceptConnection, token);
        await connTask;
        
    }
    private async Task AcceptConnection()
    {
        if (listener == null)
            throw new InvalidOperationException("Listener not initialized.");
        try
        {
            connection = await listener.AcceptConnectionAsync(token);
            Console.WriteLine($"Accepted connection from {connection.RemoteEndPoint}");
            _ = Task.Run(HandleStreamsAsync, token);
            _ = Task.Run(PingLoopAsync, token);
            _ = Task.Run(TimeoutCheckLoopAsync, token);
        }
        catch (OperationCanceledException)
        {

        }
    }

    private async Task HandleStreamsAsync()
    {
        if (connection == null)
            throw new InvalidOperationException("Connection not initialized.");
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var stream = await connection.AcceptInboundStreamAsync(token);
                    var header = new byte[1];
                    var bytesRead = await stream.ReadAsync(header.AsMemory(), token);
                    if (bytesRead == 0)
                        continue;
                
                    // I could use the stream IDs, but I prefer more control here. 
                    switch (header[0])
                    {
                        case 0x01:
                            controlStream = stream;  
                            _ = Task.Run(ControlLoopAsync, token);
                            Console.WriteLine("Opened control stream");
                            SetControlStream();
                            break;

                        case 0x02:
                            // if (!isAuthenticated)
                            // {
                            //     Console.WriteLine($"[AUTH] Rejected file stream from unauthenticated client {connection?.RemoteEndPoint}");
                            //     await stream.DisposeAsync();
                            //     break;
                            // }
                            fileStream = stream;     
                            Console.WriteLine("Opened file stream");
                            SetFileStream();
                            break;

                        default:
                            await stream.DisposeAsync();
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Connection cancelled.");
                    break;
                }
                catch (QuicException ex) when (ex.InnerException == null || ex.Message.Contains("timed out"))
                {
                    Console.WriteLine("Connection timed out due to inactivity.");
                    await StopAsync();
                } 
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Connection cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection error: {ex}");
        }
    }

    public override async Task StopAsync()
    {
        if (cts != null)
            await cts.CancelAsync(); 

        if (listener != null)
            await listener.DisposeAsync();
        
        if (connection != null)
            await connection.DisposeAsync();

        Console.WriteLine("Server stopped.");
    }
}
