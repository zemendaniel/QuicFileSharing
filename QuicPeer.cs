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
    
    private bool? isReceiver;
    protected CancellationToken token = CancellationToken.None;
    private string? saveFolder;   // sender
    private string? filePath;     // receiver
    private Dictionary<string, string>? metadata;     // receiver
    private string? joinedFilePath;   // receiver
    private Channel<string> controlSendQueue = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource bothStreamsReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool controlReady;
    private bool fileReady;
    
    private DateTime? lastKeepAliveReceived;
    private static readonly TimeSpan connectionTimeout = TimeSpan.FromSeconds(15);  // adjust if needed
    private static readonly int fileChunkSize = 16 * 1024 * 1024;
    private static readonly int messageChunkSize = 1024;
    private static readonly int fileBufferSize = 1014 * 1024;

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
    private Task WaitForStreamsAsync() => bothStreamsReady.Task;
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

        var size = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
        if (size < 0) throw new IOException("Invalid message size.");

        var payload = size == 0 ? Array.Empty<byte>() : new byte[size];
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
            case "PING":    // Both sides get this
                lastKeepAliveReceived = DateTime.UtcNow;
                break;
            case "READY":   // Receiver gets this
                Console.WriteLine("Receiver is ready, starting file send...");
                _ = Task.Run(SendFileAsync, token);
                break;
            case "RECEIVED_FILE":   // Sender gets this
                Console.WriteLine("Receiver confirmed file was received.");
                isReceiver = false;
                break;
            case "FILE_SENT":   // Receiver gets this
                Console.WriteLine("Sender confirmed file was sent.");
                break;
            default:
                if (line != null && line.StartsWith("METADATA:"))
                {
                    // todo accept or reject file
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
        await WaitForStreamsAsync();
        if (filePath == null)
            throw new InvalidOperationException("InitSend must be called first.");
        if (isReceiver == true)
            throw new InvalidOperationException("InitSend cannot be called on a receiver.");
        
        var fileInfo = new FileInfo(filePath);
        var fileName = Path.GetFileName(filePath);
        var fileSize = fileInfo.Length;
        var meta = new Dictionary<string, string>
        {
            ["FileName"] = fileName,
            ["FileSize"] = fileSize.ToString()
        };
        var json = System.Text.Json.JsonSerializer.Serialize(meta);
        await QueueControlMessage($"METADATA:{json}");
        
        await SendFileAsync();
    }
    
    private async Task SendFileAsync()
    {
        if (filePath == null) 
            throw new InvalidOperationException("InitSend must be called first.");
        if (fileStream == null) 
            throw new InvalidOperationException("File stream not initialized.");
        
        var buffer = ArrayPool<byte>.Shared.Rent(fileChunkSize);
        var hashQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        var hashTask = Task.Run(() => ComputeHashAsync(hashQueue), token);;
        
        await using var inputFile = new FileStream(
            path: filePath,
            mode: FileMode.Open,
            access: FileAccess.Read,
            share: FileShare.Read,
            bufferSize: fileBufferSize,
            useAsync: true
        );
        
        int bytesRead;
        while ((bytesRead = await inputFile.ReadAsync(buffer, token)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
            
            // we need to copy the chunk
            var chunkCopy = new byte[bytesRead];
            Array.Copy(buffer, chunkCopy, bytesRead);
            await hashQueue.Writer.WriteAsync(chunkCopy, token);
            
            Console.WriteLine($"Sent chunk: {bytesRead} bytes");
        }
        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        
        hashQueue.Writer.Complete();
        var fileHash = await hashTask;
        Console.WriteLine(fileHash);
        await QueueControlMessage("FILE_SENT");
    }

    private async Task ReceiveFileAsync()
    {
        if (metadata == null)
            throw new Exception("The receiver was started prematurely.");
        
        if (fileStream == null) 
            throw new InvalidOperationException("File stream not initialized.");
        
        if (joinedFilePath == null)
            throw new InvalidOperationException("Joined file path not initialized.");

        var buffer = ArrayPool<byte>.Shared.Rent(fileChunkSize);
        long totalBytesReceived = 0;
        var fileSize = long.Parse(metadata["FileSize"]);
        var hashQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        var hashTask = Task.Run(() => ComputeHashAsync(hashQueue), token);;
        
        await using (var outputFile = new FileStream(
                         joinedFilePath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: fileChunkSize,
                         useAsync: true))
        {
            var stopwatch = Stopwatch.StartNew();
            while (totalBytesReceived < fileSize)
            {
                var bytesRead = await fileStream.ReadAsync(buffer, token);
                if (bytesRead == 0) break;
                
                await outputFile.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                
                var chunkCopy = new byte[bytesRead];
                Array.Copy(buffer, chunkCopy, bytesRead);
                await hashQueue.Writer.WriteAsync(chunkCopy, token);
                
                totalBytesReceived += bytesRead;
                Console.WriteLine($"Received chunk: {bytesRead} bytes (total {totalBytesReceived}/{fileSize})");
            }
            hashQueue.Writer.Complete();
            var fileHash = await hashTask;
            Console.WriteLine(fileHash);
            
            stopwatch.Stop();
            await outputFile.FlushAsync(token);
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            
            Console.WriteLine($"File received and saved as {joinedFilePath}, size = {totalBytesReceived} bytes");
            Console.WriteLine(
                $"Average speed was {totalBytesReceived / (1024 * 1024) / stopwatch.Elapsed.TotalSeconds:F2} MB/s, time {stopwatch.Elapsed}");
        }

        await QueueControlMessage("RECEIVED_FILE");

        // var actualHash = await ComputeHashAsync(joinedFilePath);
        // var isValid = string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);
        // Console.WriteLine(isValid ? "File integrity confirmed." : "[ERROR] File integrity failed.");

        metadata = null;
        joinedFilePath = null;
    }

    private async Task QueueControlMessage(string msg)
    {
        await controlSendQueue.Writer.WriteAsync(msg, token);
    }
    private async Task<string> ComputeHashAsync(Channel<byte[]> hashQueue)
    {
        Console.WriteLine("Calculating hash...");
        using var sha = SHA256.Create();
        await foreach (var chunk in hashQueue.Reader.ReadAllAsync(token))
            sha.TransformBlock(chunk, 0, chunk.Length, null, 0);
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash);
    }
    public abstract Task StartAsync(int port = 5000);
    public abstract Task StopAsync();
    
    protected async Task PingLoopAsync()
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(connectionTimeout, token);
            await QueueControlMessage("PING");
        }
    }

    protected async Task TimeoutCheckLoopAsync()
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            if (DateTime.UtcNow - lastKeepAliveReceived > connectionTimeout)
            {
                Console.WriteLine("[ERROR] Connection timed out.");
                await StopAsync();
                break;
            }
        }
    }
}
