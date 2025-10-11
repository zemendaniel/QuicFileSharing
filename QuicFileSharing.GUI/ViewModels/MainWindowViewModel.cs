using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic.CompilerServices;
using QuicFileSharing.Core;
using QuicFileSharing.GUI.Models;
using Avalonia.Styling;

namespace QuicFileSharing.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string WsBaseUri = "ws://vps.zemendaniel.hu:8080";
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

    private Server? server;
    private Client? client;
    private readonly SignalingUtils signalingUtils = new();
    // private WebSocketSignaling signaling;
    

    public MainWindowViewModel()
    {
        
    }

    [RelayCommand]
    private async Task JoinRoom()
    {
        client = new Client();
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
        var (success, errorMessage) = await Task.Run(() => signaling.ConnectAsync(Role.Client, RoomCode.Trim().ToLower()), cts.Token);
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

    [RelayCommand]
    private async Task CreateRoom()
    {
        server = new Server();
        LobbyText = "Connecting to coordination server...";

        await using var signaling = new WebSocketSignaling(WsBaseUri);
        
        signaling.OnDisconnected += (_, description) =>
        {
            if (server.ClientConnected.Task.IsCompleted) return;
            
            State = AppState.Lobby;
            LobbyText = $"Disconnected from coordination server: {(string.IsNullOrEmpty(description) ?
                "The signaling was closed before your peer could join." : description)}";
        };
        var (success, errorMessage) = await Task.Run(() => signaling.ConnectAsync(Role.Server));
        // var (success, errorMessage) = await signaling.ConnectAsync(Role.Server, RoomCode);
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
            signalingUtils.ClientThumbprint!));;
        
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
}
