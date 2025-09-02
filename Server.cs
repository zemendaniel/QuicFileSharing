using System;
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
            _ = HandleConnectionAsync(connection); 
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
