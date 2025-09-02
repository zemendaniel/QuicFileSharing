using System;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Text;


public class Client
{
    public async Task ConnectAndSendMessageAsync()
    {
        var clientConnectionOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5000),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = new List<SslApplicationProtocol>
                {
                    new SslApplicationProtocol("fileShare")
                },
                TargetHost = "", // The server hostname for TLS validation
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            }
        };

        // Establish connection to the server.
        var connection = await QuicConnection.ConnectAsync(clientConnectionOptions);
        Console.WriteLine($"Connected to {connection.RemoteEndPoint}");

        // Open a bidirectional stream.
        var outboundStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
        await Task.Delay(0);
        // Send a message to the server.
        string message = "Hello from client!";
        byte[] data = Encoding.UTF8.GetBytes(message);
        await outboundStream.WriteAsync(data);

        // Ensure the write side is completed.
        await outboundStream.WriteAsync(data, completeWrites: true);

        // Now, listen for the server's response.
        byte[] buffer = new byte[1024];
        int count = await outboundStream.ReadAsync(buffer);
        string response = Encoding.UTF8.GetString(buffer, 0, count);
        Console.WriteLine("Received from server: " + response);

        // Dispose the connection and stream when done.
        await outboundStream.DisposeAsync();
        await connection.DisposeAsync();
    }
    public async Task SendFileAsync(string filePath, string host = "127.0.0.1", int port = 5000)
    {
        var options = new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Parse(host), port),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol("fileShare")],
                RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
            }
        };

        await using var connection = await QuicConnection.ConnectAsync(options);
        using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);

        const int chunkSize = 1024 * 1024; // 1 MB
        byte[] buffer = new byte[chunkSize];

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        int bytesRead;
        while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, bytesRead));
            Console.WriteLine($"Sent chunk: {bytesRead} bytes");
        }

        await stream.WriteAsync(Array.Empty<byte>(), completeWrites: true);
        Console.WriteLine("File sent.");

        // optional: wait for server response
        byte[] responseBuffer = new byte[1024];
        int responseBytes = await stream.ReadAsync(responseBuffer);
        string response = Encoding.UTF8.GetString(responseBuffer, 0, responseBytes);
        Console.WriteLine($"Server response: {response}");
    }
}