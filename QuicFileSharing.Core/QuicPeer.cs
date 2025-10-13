using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Net.Quic;
using System.Text;
using System.Diagnostics;
using System.Buffers;
using System.Buffers.Binary;
using System.Threading.Channels;

namespace QuicFileSharing.Core;

// public delegate Task<(bool, Uri?)> OnFileOffered(string filePath, long fileSize);

public abstract class QuicPeer
{
    protected readonly X509Certificate2 cert = CreateSelfSignedCertificate();
    public string Thumbprint => cert.Thumbprint;

    protected QuicConnection? connection;
    protected QuicStream? controlStream;
    protected QuicStream? fileStream;
    protected CancellationTokenSource? cts;
    
    public bool IsSending { get; set; }
    protected CancellationToken token = CancellationToken.None;
    private Uri? saveFolder; // sender
    private Uri? filePath; // receiver
    private Dictionary<string, string>? metadata; // receiver
    private string? joinedFilePath; // receiver
    private Channel<string> controlSendQueue = Channel.CreateUnbounded<string>();

    private readonly TaskCompletionSource bothStreamsReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<bool>? FileTransferCompleted { get; private set; }

    private TaskCompletionSource<string>? fileHashReady;
    private bool controlReady;
    private bool fileReady;
    private bool isTransferInProgress;
    
    // private TaskCompletionSource<bool> fileAcceptanceTsc = new();

    private DateTime? lastKeepAliveReceived;
    private static readonly TimeSpan connectionTimeout = TimeSpan.FromSeconds(15); // adjust if needed
    private static readonly TimeSpan pingInterval = TimeSpan.FromSeconds(2); // adjust if needed
    private static readonly int fileChunkSize = 16 * 1024 * 1024;
    private static readonly int messageChunkSize = 1024;
    private static readonly int fileBufferSize = 1014 * 1024;
    
    // public OnFileOffered OnFileOffered { get; set; }

    // public event Action? ConnectionReady;
    public event Action? OnDisconnected;
    public event Action<string>? OnFileRejected;
    public event Action<string, long>? OnFileOffered;
    public TaskCompletionSource<(bool, Uri?)> FileOfferDecisionTsc { get; private set; } = new();

    public void SetReceivePath(Uri folder)
    {
        saveFolder = folder;
    }

    public void SetSendPath(Uri path)
    {
        filePath = path;
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
                _ = Task.Run(() => HandleControlMessage(message), token);
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

    private void ResetAfterFileTransferCompleted()
    {
        Console.WriteLine("Resetting after file transfer completed.");
        IsSending = false;
        metadata = null;
        joinedFilePath = null;
        fileHashReady = null;
        filePath = null;
        saveFolder = null;
        isTransferInProgress = false;
    }
    
    private async Task HandleControlMessage(string? line)
    {
        Console.WriteLine(line);
        switch (line)
        {
            case null:
                break;
            case "PING": // Both sides get this
                lastKeepAliveReceived = DateTime.UtcNow;
                break;
            case "READY": // Receiver gets this
                Console.WriteLine("Receiver is ready, starting file send...");
                _ = Task.Run(SendFileAsync, token);
                break;
            case var s when line.StartsWith("RECEIVED_FILE:"): // Sender gets this, marks the end of file transfer
                var status = line["RECEIVED_FILE:".Length..];
                switch (status)
                {
                    case "OK":
                        Console.WriteLine("Receiver confirmed file was received successfully.");
                        FileTransferCompleted?.SetResult(true);
                        break;
                    case "FAILED":
                        Console.WriteLine("Receiver did not receive the file successfully (integrity check failed).");
                        FileTransferCompleted?.SetResult(false);
                        break;
                    default:
                        Console.WriteLine($"Unknown status: {status}");
                        break;
                }
                ResetAfterFileTransferCompleted();
                break;
            
            case var s when line.StartsWith("METADATA:"):   // Receiver gets this, marks the start of file transfer
                if (isTransferInProgress)
                {
                    await QueueControlMessage("REJECTED:ALREADY_RECEIVING");
                    return;
                }
                isTransferInProgress = true;

                if (IsSending)
                {
                    await QueueControlMessage("REJECTED:ALREADY_SENDING");
                    return;
                }
                
                FileTransferCompleted = new();
                
                var json = line["METADATA:".Length..];
                metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
                Console.WriteLine($"Received metadata: {string.Join(", ", metadata)}");
                
                FileOfferDecisionTsc = new(TaskCreationOptions.RunContinuationsAsynchronously);
                OnFileOffered?.Invoke(metadata["FileName"], long.Parse(metadata["FileSize"]));
                var (accepted, path) = await FileOfferDecisionTsc.Task;
                
                if (!accepted)
                {
                    await QueueControlMessage("REJECTED:UNWANTED");
                    return;
                }
                saveFolder = path;
                
                if (saveFolder == null)
                    throw new InvalidOperationException("Save folder not initialized.");
                joinedFilePath = Path.Combine(saveFolder.AbsolutePath, metadata["FileName"]);
                fileHashReady = new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                await QueueControlMessage("READY");
                _ = Task.Run(ReceiveFileAsync, token);
                break;
            case var s when line.StartsWith("FILE_SENT:"): // Receiver gets this
                Console.WriteLine("Sender confirmed file was sent.");
                if (fileHashReady == null)
                    throw new InvalidOperationException("File hash ready not initialized.");
                var hash = line["FILE_SENT:".Length..];
                Console.WriteLine($"Received file hash: {hash}");
                fileHashReady.SetResult(hash);
                break;
            case var s when s.StartsWith("REJECTED:"): 
                var reason = s["REJECTED:".Length..];
                switch (reason)
                {
                    case "ALREADY_SENDING":
                        OnFileRejected?.Invoke("File rejected: Peer is already sending or preparing to send a file.");
                        break;
                    case "UNWANTED":
                        OnFileRejected?.Invoke("File rejected: Peer does not want this file.");
                        break;
                }
                break;
            default:
                Console.WriteLine($"Unknown control message: {line}");
                break;
        }
    }

    public async Task StartSending()
    {
        FileTransferCompleted = new();
        await WaitForStreamsAsync();
        if (filePath == null)
            throw new InvalidOperationException("InitSend must be called first.");
        if (!IsSending)
            throw new InvalidOperationException("InitSend cannot be called on a receiver.");

        var fileInfo = new FileInfo(filePath.AbsolutePath);
        var fileName = Path.GetFileName(filePath.AbsolutePath);
        var fileSize = fileInfo.Length;
        var meta = new Dictionary<string, string>
        {
            ["FileName"] = fileName,
            ["FileSize"] = fileSize.ToString()
        };
        var json = System.Text.Json.JsonSerializer.Serialize(meta);
        await QueueControlMessage($"METADATA:{json}");
    }

    private async Task SendFileAsync()
    {
        if (filePath == null)
            throw new InvalidOperationException("InitSend must be called first.");
        if (fileStream == null)
            throw new InvalidOperationException("File stream not initialized.");
        if (isTransferInProgress)
            return;
        
        isTransferInProgress = true;

        var hashQueue = Channel.CreateBounded<ArraySegment<byte>>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        var hashTask = Task.Factory.StartNew(() => ComputeHashAsync(hashQueue), TaskCreationOptions.LongRunning)
            .Unwrap();

        await using var inputFile = new FileStream(
            path: filePath.AbsolutePath,
            mode: FileMode.Open,
            access: FileAccess.Read,
            share: FileShare.Read,
            bufferSize: fileBufferSize,
            useAsync: true
        );

        while (true)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(fileChunkSize);
            var bytesRead = await inputFile.ReadAsync(buffer.AsMemory(0, fileChunkSize), token);
            if (bytesRead == 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
            await hashQueue.Writer.WriteAsync(new ArraySegment<byte>(buffer, 0, bytesRead), token);
            Console.WriteLine($"Sent chunk: {bytesRead} bytes");
        }

        hashQueue.Writer.Complete();
        var fileHash = await hashTask;
        Console.WriteLine(fileHash);
        await QueueControlMessage($"FILE_SENT:{fileHash}");
    }

    private async Task ReceiveFileAsync()
    {
        if (metadata == null)
            throw new Exception("The receiver was started prematurely.");

        if (fileStream == null)
            throw new InvalidOperationException("File stream not initialized.");

        if (joinedFilePath == null)
            throw new InvalidOperationException("Joined file path not initialized.");

        long totalBytesReceived = 0;
        var fileSize = long.Parse(metadata["FileSize"]);

        var hashQueue = Channel.CreateBounded<ArraySegment<byte>>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        var hashTask = Task.Factory.StartNew(() => ComputeHashAsync(hashQueue), TaskCreationOptions.LongRunning)
            .Unwrap();

        await using var outputFile = new FileStream(
            joinedFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: fileChunkSize,
            useAsync: true);

        var stopwatch = Stopwatch.StartNew();

        while (totalBytesReceived < fileSize)
        {
            // So we basically don't have to copy the chunk to a new buffer, we can just overwrite the existing one
            // after we read it.
            var buffer = ArrayPool<byte>.Shared.Rent(fileChunkSize);
            var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, fileChunkSize), token);
            if (bytesRead == 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                break;
            }

            await outputFile.WriteAsync(buffer.AsMemory(0, bytesRead), token);

            await hashQueue.Writer.WriteAsync(new ArraySegment<byte>(buffer, 0, bytesRead), token);

            totalBytesReceived += bytesRead;
            Console.WriteLine($"Received chunk: {bytesRead} bytes (total {totalBytesReceived}/{fileSize})");
        }

        hashQueue.Writer.Complete();

        var actualFileHash = await hashTask;
        Console.WriteLine($"SHA256: {actualFileHash}");

        stopwatch.Stop();
        await outputFile.FlushAsync(token);

        var expectedFileHash = await fileHashReady!.Task;
        var success = actualFileHash == expectedFileHash;
        if (!success)
        {
            Console.WriteLine("[ERROR] File integrity check failed.");
            await QueueControlMessage("RECEIVED_FILE:FAILED");
        }
        else
        {
            Console.WriteLine("[SUCCESS] File received successfully.");
            await QueueControlMessage("RECEIVED_FILE:OK");
        }
        FileTransferCompleted.SetResult(success);
        Console.WriteLine($"File was saved as {joinedFilePath}, size = {totalBytesReceived} bytes");
        Console.WriteLine(
            $"Average speed was {totalBytesReceived / (1024 * 1024) / stopwatch.Elapsed.TotalSeconds:F2} MB/s, time {stopwatch.Elapsed}");
        ResetAfterFileTransferCompleted();
    }


    private async Task QueueControlMessage(string msg)
    {
        await controlSendQueue.Writer.WriteAsync(msg, token);
    }

    private async Task<string> ComputeHashAsync(Channel<ArraySegment<byte>> hashQueue)
    {
        Console.WriteLine("Calculating hash...");
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await foreach (var segment in hashQueue.Reader.ReadAllAsync(token))
        {
            hasher.AppendData(segment.AsSpan());
            ArrayPool<byte>.Shared.Return(segment.Array!);
        }

        var hash = hasher.GetHashAndReset();
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public abstract Task StopAsync();

    protected async Task PingLoopAsync()
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(pingInterval, token);
            await QueueControlMessage("PING");
        }
    }

    protected async Task TimeoutCheckLoopAsync()
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(pingInterval, token);
            if (DateTime.UtcNow - lastKeepAliveReceived > connectionTimeout)
            {
                Console.WriteLine("[ERROR] Connection timed out.");
                OnDisconnected?.Invoke();
                await StopAsync();
                break;
            }
        }
    }

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
        var notAfter = notBefore.AddYears(100);
        var cert = request.CreateSelfSigned(notBefore, notAfter);

        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }
}
