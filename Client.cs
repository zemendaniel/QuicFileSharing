using System;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Text;


public class Client : QuicPeer
{
    public async Task StartAsync()
    {
        var clientConnectionOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5000),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [new("fileShare")],
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
        //_ = Task.Run(FileLoopAsync);
        
        _ = Task.Run(KeepAliveLoopAsync);
        
    }
    private async Task KeepAliveLoopAsync()
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            await QueueControlMessage("PING");
        }
    }

    public async Task StopAsync()
    {
        if (cts != null)
            await cts.CancelAsync(); 
        
        if (connection != null)
            await connection.DisposeAsync();

        Console.WriteLine("Client stopped.");
    }

    // private async Task TestAsync()
    // {
    //     Console.WriteLine("Sending");
    //     await controlSendQueue.Writer.WriteAsync("123", token);
    // }
}
