using System.Net.Quic;
using System.Text;

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
    protected CancellationToken token = CancellationToken.None;


    protected Task ControlLoopAsync()
    {
        return Task.CompletedTask;
    }
    
    protected Task FileLoopAsync()
    {
        return Task.CompletedTask;
    }
}
