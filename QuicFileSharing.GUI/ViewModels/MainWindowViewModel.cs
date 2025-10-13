using System;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic.CompilerServices;
using QuicFileSharing.Core;
using QuicFileSharing.GUI.Models;
using Avalonia.Styling;
using QuicFileSharing.GUI.Views;


namespace QuicFileSharing.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string WsBaseUri = "ws://152.53.123.174:8080";
    [ObservableProperty]
    private AppState state = AppState.Lobby;
    [ObservableProperty]
    private string roomCode = string.Empty;
    [ObservableProperty]
    private string roomLoadingMessage = string.Empty;
    [ObservableProperty]
    private string statusMessage = string.Empty;
    [ObservableProperty]
    private string lobbyText = string.Empty;
    
    private QuicPeer peer;
   
    
    [RelayCommand]
    private async Task JoinRoom()
    {
        peer = new Client(); 
        SetPeerHandlers();
        var client = (peer as Client)!;
        
        var signalingUtils = new SignalingUtils();
        await using var signaling = new WebSocketSignaling(WsBaseUri);
        
        var cts = new CancellationTokenSource();

        signaling.OnDisconnected += async (_, description) =>
        {
            if (client.GotConnected) return;
            if (cts.Token.IsCancellationRequested) return;
            await cts.CancelAsync();
            State = AppState.Lobby;
            LobbyText = $"Disconnected from coordination server: {description ?? "Unknown error"}";
        };
        LobbyText = "Connecting to coordination server...";
        try
        {
            var (success, errorMessage) =
                await Task.Run(() => signaling.ConnectAsync(Role.Client, RoomCode.Trim().ToLower()), cts.Token);
            if (success is not true)
            {
                State = AppState.Lobby;
                LobbyText = $"Could not connect to coordination server: {errorMessage}";
                return;
            }

            var offer = await Task.Run(() => signalingUtils.ConstructOfferAsync(client.Thumbprint), cts.Token);

            try
            {
                await Task.Run(() => signaling.SendAsync(offer, "offer"), cts.Token);
            }
            catch (InvalidOperationException ex)
            {
                LobbyText = $"Could not connect to coordination server: {ex.Message}";
            }

            var answer = await signaling.AnswerTsc.Task;
            LobbyText = "Connecting to peer...";
            signalingUtils.ProcessAnswer(answer);
            if (signalingUtils.ChosenPeerIp == null)
            {
                LobbyText = "Could not connect to peer: Could not agree on IP generation.";
                return;
            }

            try
            {
                await Task.Run(() => client.StartAsync(
                    signalingUtils.ChosenPeerIp,
                    signalingUtils.ChosenPeerPort,
                    signalingUtils.IsIpv6,
                    signalingUtils.ChosenOwnPort,
                    signalingUtils.ServerThumbprint!), cts.Token);
            }
            catch (Exception ex)
            {

                await cts.CancelAsync();
                LobbyText = "Could not connect to peer: " + ex.Message;
                return;
            }

            State = AppState.InRoom;
            await Task.Run(signaling.CloseAsync, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    [RelayCommand]
    private async Task CreateRoom()
    {
        peer = new Server();
        SetPeerHandlers();

        LobbyText = "Connecting to coordination server...";
        var server = (peer as Server)!;

        var signalingUtils = new SignalingUtils();
        await using var signaling = new WebSocketSignaling(WsBaseUri);
        
        signaling.OnDisconnected += (_, description) =>
        {
            if (server.ClientConnected.Task.IsCompleted) return;
            
            State = AppState.Lobby;
            LobbyText = $"Disconnected from coordination server: {(string.IsNullOrEmpty(description) ?
                "The signaling was closed before your peer could join." : description)}";
        };
        var (success, errorMessage) = await Task.Run(() => signaling.ConnectAsync(Role.Server));
        if (success is not true)
        { 
            State = AppState.Lobby; 
            LobbyText = $"Could not connect to coordination server: {errorMessage}";
            return;
        }        
        var info = await signaling.RoomInfoTcs.Task;
        
        State = AppState.WaitingForConnection;
        RoomCode = info.id;

        var offer = await signaling.OfferTcs.Task;
        var answer = await Task.Run(() => signalingUtils.ConstructAnswerAsync(offer, server.Thumbprint));
        await Task.Run(() => server.StartAsync(signalingUtils.IsIpv6, signalingUtils.ChosenOwnPort,
            signalingUtils.ClientThumbprint!));
        
        try
        {
            await Task.Run(() => signaling.SendAsync(answer, "answer"));
        }
        catch (InvalidOperationException ex)
        {
            State = AppState.Lobby;
            LobbyText = $"Could not connect to coordination server: {ex.Message}";
        }

        await server.ClientConnected.Task;
        State = AppState.InRoom;
        await Task.Run(signaling.CloseAsync);
    }

    [RelayCommand]
    private async Task SendFile(Window window) 
    {
        peer.IsSending = true;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select file to send",
            AllowMultiple = false
        });

        if (files.Count == 0) return;
        var file = files[0];
        peer.SetSendPath(file.Path);
        await peer.StartSending();
        var success = await peer.FileTransferCompleted.Task;
        Console.WriteLine("File transfer completed.");
    }

    private void SetPeerHandlers()
    {
        peer.OnDisconnected += () =>
        {
            State = AppState.Lobby;
            LobbyText = "Connection Error: You got disconnected from your peer.";
        };
        peer.OnFileOffered += async (fileName, fileSize) =>
        {
            await Dispatcher.UIThread.InvokeAsync( async () =>
            {
                Console.WriteLine("file offered");
                var dialog = new FileOfferDialog
                {
                    DataContext = new FileOfferDialogViewModel(fileName, fileSize)
                };
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    Console.WriteLine("showing dialog");
                    
                    var result = await dialog.ShowDialog<(bool accepted, Uri? path)>(desktop.MainWindow);
                    Console.WriteLine("got result");
                    peer.FileOfferDecisionTsc.SetResult(result);
                }
            });
        };
    }
}
