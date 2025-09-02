using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public class Server
{
    private bool isRunning = true;
    private QuicListener? listener;

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using (var rsa = new RSACryptoServiceProvider(2048))
        {
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
    }

    public async Task StartAsync(int port = 5000)
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

        while (isRunning)
        {
            var connection = await listener.AcceptConnectionAsync();
            //_ = HandleConnectionAsync(connection); 
            _ = HandleFileAsync(connection); 
        }
    }

    private async Task HandleConnectionAsync(QuicConnection connection)
    {
        try
        {
            var stream = await connection.AcceptInboundStreamAsync();

            var receivedData = new List<byte>();
            byte[] buffer = new byte[1024];

            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                receivedData.AddRange(buffer.AsSpan(0, bytesRead).ToArray());
            }

            string receivedMessage = Encoding.UTF8.GetString(receivedData.ToArray());
            Console.WriteLine($"Received: {receivedMessage}");

            string responseMessage = "Hello from server!";
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseMessage);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length);

            Console.WriteLine("Connection handled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection error: {ex.Message}");
        }
    }
    private async Task HandleFileAsync(QuicConnection connection)
    {
        try
        {
            var stream = await connection.AcceptInboundStreamAsync();

            string outputPath = "/root/big2.zip";
            using var outputFile = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true);

            byte[] buffer = new byte[1024 * 1024]; // 1 MB chunks
            int bytesRead;
            long totalBytes = 0;
            var stopwatch = Stopwatch.StartNew();
            var chunkStopwatch = Stopwatch.StartNew();
            double avgSpeed = 0;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await outputFile.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalBytes += bytesRead;
                //double instSpeed = (bytesRead / (1024.0 * 1024.0)) / chunkStopwatch.Elapsed.TotalSeconds;
                avgSpeed = (totalBytes / (1024.0 * 1024.0)) / stopwatch.Elapsed.TotalSeconds;
                
                chunkStopwatch.Restart();
                //Console.WriteLine($"Received chunk: {bytesRead} bytes (total {totalBytes} bytes)");
                //Console.WriteLine($"Instant speed: {instSpeed:F2} MB/s");
            }
            stopwatch.Stop();   
            // make sure everything is flushed to disk
            await outputFile.FlushAsync();

            Console.WriteLine($"✅ File received and saved as {outputPath}, size = {totalBytes} bytes");
            Console.WriteLine($"Average speed was {avgSpeed:F2} MB/s, time {stopwatch.Elapsed}");
            // Send confirmation
            string responseMessage = "File received successfully!";
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseMessage);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
            await stream.FlushAsync(); // signal end of response
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection error: {ex.Message}");
        }
    }


    public async Task Stop()
    {
        isRunning = false;
        if (listener is not null)
        {
            await listener.DisposeAsync();
        }
        Console.WriteLine("Server stopped.");
    }
}

// Example usage:
// var server = new Server();
// await server.StartAsync(5000);
