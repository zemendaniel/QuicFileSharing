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

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = new RSACryptoServiceProvider(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        var notBefore = DateTime.UtcNow;
        var notAfter = notBefore.AddYears(1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    public async Task StartAsync(int port = 5000)
    {
        var serverConnectionOptions = new QuicServerConnectionOptions
        {
            // IdleTimeout = TimeSpan.FromSeconds(15),
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
        
        while (!token.IsCancellationRequested)
        {
            try
            {
                connection = await listener.AcceptConnectionAsync(token);
                Console.WriteLine($"Accepted connection from {connection.RemoteEndPoint}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            _ = Task.Run(HandleConnectionAsync);
        }
    }

    private async Task HandleConnectionAsync()
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var stream = await connection.AcceptInboundStreamAsync(token);
                    var header = new byte[1];
                    int bytesRead = await stream.ReadAsync(header.AsMemory(), token);
                    if (bytesRead == 0)
                        continue;
                
                    // I could use the stream IDs, but I prefer more control here. 
                    switch (header[0])
                    {
                        case 0x01:
                            _ = Task.Run(ControlLoopAsync, token);
                            Console.WriteLine("Opened control stream");
                            break;
                        case 0x02:
                            _ = Task.Run(FileLoopAsync, token);
                            Console.WriteLine("Opened file stream");
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
    // private async Task HandleFileAsync()
    // {
    //     try
    //     {
    //         var stream = await connection.AcceptInboundStreamAsync();
    //
    //         string outputPath = "/root/big2.zip";
    //         await using var outputFile = new FileStream(
    //             outputPath,
    //             FileMode.Create,
    //             FileAccess.Write,
    //             FileShare.None,
    //             bufferSize: QuicFileSharing.fileChunkSize,
    //             useAsync: true);
    //
    //         byte[] buffer = ArrayPool<byte>.Shared.Rent(QuicFileSharing.fileChunkSize);
    //         long totalBytes = 0;
    //         int bytesRead;
    //         var stopwatch = Stopwatch.StartNew();
    //         while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
    //         {
    //             await outputFile.WriteAsync(buffer.AsMemory(0, bytesRead));
    //             totalBytes += bytesRead;
    //             
    //             // double instSpeed = (bytesRead / (1024.0 * 1024.0)) / chunkStopwatch.Elapsed.TotalSeconds;
    //             // avgSpeed = (totalBytes / (1024.0 * 1024.0)) / stopwatch.Elapsed.TotalSeconds;
    //             //Console.WriteLine($"Instant speed: {instSpeed:F2} MB/s");
    //             
    //             Console.WriteLine($"Received chunk: {bytesRead} bytes (total {totalBytes} bytes)");
    //         }
    //         stopwatch.Stop();   
    //         await outputFile.FlushAsync();
    //         ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
    //         
    //         Console.WriteLine($"File received and saved as {outputPath}, size = {totalBytes} bytes");
    //         Console.WriteLine($"Average speed was {(totalBytes / (1024 * 1024) / stopwatch.Elapsed.TotalSeconds):F2} MB/s, time {stopwatch.Elapsed}");
    //         
    //         string responseMessage = "File received successfully!";
    //         byte[] responseBytes = Encoding.UTF8.GetBytes(responseMessage);
    //         await stream.WriteAsync(responseBytes.AsMemory(), completeWrites:  true);
    //         await stream.FlushAsync(); 
    //     }
    //     catch (Exception ex)
    //     {
    //         Console.WriteLine($"Connection error: {ex.Message}");
    //     }
    // }

    public async Task StopAsync()
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
