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
}