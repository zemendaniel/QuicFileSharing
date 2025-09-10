using System.Net.Quic;
using System.Text;
using System.Diagnostics;
using System.Buffers;
using System.Buffers.Binary;
using System.Threading.Channels;
using System.Security.Cryptography;

public abstract class QuicPeer
{
    protected QuicConnection? connection;

    protected QuicStream? controlStream;
    protected QuicStream? fileStream;

    protected CancellationTokenSource? cts;

    // protected bool isRunning = false;
    // protected bool isReceivingFile = false;
    // protected bool isSendingFile = false;
    // protected readonly List<Task> connectionTasks = new();
    protected bool isReceiver = false;
    protected CancellationToken token = CancellationToken.None;
    protected string? saveFolder;
    protected string? filePath;
    protected Dictionary<string, string>? metadata;
    protected string? joinedFilePath;
    protected Channel<string> controlSendQueue = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource bothStreamsReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool controlReady = false;
    private bool fileReady = false;

    public bool IsReceiver => isReceiver;

    public void InitReceive(string folder)
    {
        saveFolder = folder;
        isReceiver = true;
    }

    public void InitSend(string path)
    {
        filePath = path;
        isReceiver = false;
    }
    protected void SetControlStream()
    {
        controlReady = true;
        CompleteIfBothStreamsReady();
    }

    protected void SetFileStream()
    {
        fileReady = true;
        CompleteIfBothStreamsReady();
    }
    protected Task WaitForStreamsAsync() => bothStreamsReady.Task;
    private void CompleteIfBothStreamsReady()
    {
        if (controlReady && fileReady)
        {
            bothStreamsReady.TrySetResult();
        }
    }

    protected async Task ControlLoopAsync()
    {
        if (controlStream == null)
            throw new InvalidOperationException("Control stream not initialized.");

        var sendTask = Task.Run(async () =>
        {
            await foreach (var msg in controlSendQueue.Reader.ReadAllAsync(token))
            {
                var payload = Encoding.UTF8.GetBytes(msg);
                await SendMessageAsync(payload);
            }
        }, token);

        var receiveTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var payload = await ReadMessageAsync();
                if (payload == null) break; // stream closed
                var message = Encoding.UTF8.GetString(payload);
                await HandleControlMessage(message);
            }
        }, token);

        await Task.WhenAll(sendTask, receiveTask);
    }
    
    private async Task SendMessageAsync(ReadOnlyMemory<byte> payload)
    {
        if (controlStream == null) throw new InvalidOperationException("Control stream not initialized.");

        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBuf, payload.Length);

        await controlStream.WriteAsync(lenBuf, token);
        if (!payload.IsEmpty)
            await controlStream.WriteAsync(payload, token);
        await controlStream.FlushAsync(token);
    }
    
    private async Task<byte[]?> ReadMessageAsync()
    {
        if (controlStream == null) throw new InvalidOperationException("Control stream not initialized.");

        var lenBuf = new byte[4];
        try
        {
            await controlStream.ReadExactlyAsync(lenBuf, token);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        int size = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
        if (size < 0) throw new IOException("Invalid message size.");

        byte[] payload = size == 0 ? Array.Empty<byte>() : new byte[size];
        if (size > 0)
        {
            await controlStream.ReadExactlyAsync(payload, token);
        }

        return payload;
    }



    private async Task HandleControlMessage(string? line)
    {
        switch (line)
        {
            case "PING":
                await QueueControlMessage("PONG");
                break;
            case "PONG":
                break;
            case "READY":
                Console.WriteLine("Receiver is ready, starting file send...");
                _ = Task.Run(SendFileAsync, token);
                break;
            case "RECEIVED":
                Console.WriteLine("Receiver confirmed file was received.");
                break;
            default:
                if (line != null && line.StartsWith("METADATA:"))
                {
                    if (saveFolder == null)
                        throw new InvalidOperationException("Save folder not initialized.");
                    var json = line["METADATA:".Length..];
                    metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
                    Console.WriteLine($"Received metadata: {string.Join(", ", metadata)}");
                    joinedFilePath = Path.Combine(saveFolder, metadata["FileName"]);
                    await QueueControlMessage("READY");
                    _ = Task.Run(ReceiveFileAsync, token);
                }
                else
                {
                    Console.WriteLine($"Unknown control message: {line}");
                }
                break;
        }
    }

    public async Task StartSending()
    {
        if (filePath == null)
            throw new InvalidOperationException("InitSend must be called first.");
        
        var fileInfo = new FileInfo(filePath);
        var fileName = Path.GetFileName(filePath);
        long fileSize = fileInfo.Length;
        Console.WriteLine("Calculating hash...");
        var hash = await ComputeHashAsync(filePath);
        var meta = new Dictionary<string, string>
        {
            ["FileName"] = fileName,
            ["FileSize"] = fileSize.ToString(),
            ["FileHash"] = hash
        };
        var json = System.Text.Json.JsonSerializer.Serialize(metadata);
        await QueueControlMessage($"METADATA:{json}");
        
        await SendFileAsync();
    }

    public async Task StartReceiving()
    {
        // todo
    }
    
    protected async Task SendFileAsync()
    {
        // todo
        // todo mark end of file
    }

    protected async Task ReceiveFileAsync()
    {

        if (metadata == null)
            throw new Exception("The receiver was started prematurely.");

        await using var outputFile = new FileStream(
            joinedFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: QuicFileSharing.fileChunkSize,
            useAsync: true);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(QuicFileSharing.fileChunkSize);
        long totalBytesReceived = 0;
        int bytesRead;
        var stopwatch = Stopwatch.StartNew();
        while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
        {
            await outputFile.WriteAsync(buffer.AsMemory(0, bytesRead), token);
            totalBytesReceived += bytesRead;
            Console.WriteLine($"Received chunk: {bytesRead} bytes (total {totalBytesReceived})");
        }
        stopwatch.Stop();   
        await outputFile.FlushAsync();
        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        Console.WriteLine($"File received and saved as {joinedFilePath}, size = {totalBytesReceived} bytes");
        Console.WriteLine($"Average speed was {totalBytesReceived / (1024 * 1024) / stopwatch.Elapsed.TotalSeconds:F2} MB/s, time {stopwatch.Elapsed}");
        
        await QueueControlMessage("DONE");
    }

    protected async Task QueueControlMessage(string msg)
    {
        await controlSendQueue.Writer.WriteAsync(msg, token);
    }
    private async Task<string> ComputeHashAsync(string path)
    {
        using var sha = SHA256.Create();
        await using var stream = File.OpenRead(path);
        var buffer = new byte[QuicFileSharing.fileChunkSize]; 
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, token)) > 0)
        {
            sha.TransformBlock(buffer, 0, bytesRead, null, 0);
        }
        
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash);
    }
}
