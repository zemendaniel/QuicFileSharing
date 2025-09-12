using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public class Server: QuicPeer
{

    private QuicListener? listener;
    private Task? acceptLoopTask;
    private DateTime lastPongReceived = DateTime.UtcNow;

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = new RSACryptoServiceProvider(2048);
        var request = new CertificateRequest(
            "",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        var notBefore = DateTime.UtcNow;
        var notAfter = notBefore.AddYears(1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    public override async Task StartAsync(int port = 5000)
    {
        var serverConnectionOptions = new QuicServerConnectionOptions
        {
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            ServerAuthenticationOptions = new SslServerAuthenticationOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol("fileShare")],
                ServerCertificate = CreateSelfSignedCertificate()
            }
        };

        listener = await QuicListener.ListenAsync(new QuicListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, port),
            ApplicationProtocols = [new SslApplicationProtocol("fileShare")],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(serverConnectionOptions)
        });
        Console.WriteLine($"Server listening on 127.0.0.1:{port}");
        
        cts = new CancellationTokenSource();
        token = cts.Token;
        
        acceptLoopTask = Task.Run(AcceptConnectionsLoop, token);
        
        _ = Task.Run(PingLoopAsync, token);
        _ = Task.Run(TimeoutCheckLoopAsync, token);

        
    }
    private async Task AcceptConnectionsLoop()
    {
        if (listener == null)
            throw new InvalidOperationException("Listener not initialized.");
        try
        {
            while (!token.IsCancellationRequested)
            {
                connection = await listener.AcceptConnectionAsync(token);
                Console.WriteLine($"Accepted connection from {connection.RemoteEndPoint}");
                _ = Task.Run(HandleConnectionAsync, token);
            }
        }
        catch (OperationCanceledException)
        {

        }
    }

    private async Task HandleConnectionAsync()
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
    // todo check file hash while sending/receiving
}
