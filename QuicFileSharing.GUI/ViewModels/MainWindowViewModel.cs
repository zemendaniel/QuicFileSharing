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
        
        var gotDisconnected = false;

        signaling.OnDisconnected += (_, description) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                State = AppState.Lobby;
                LobbyText = $"Disconnected from coordination server: {description ?? "Unknown error"}";
            });
            gotDisconnected = true;
        };
        LobbyText = "Connecting to coordination server...";
        var (success, errorMessage) = await signaling.ConnectAsync(Role.Client, RoomCode);
        if (success is not true)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                State = AppState.Lobby;
                LobbyText = $"Could not connect to coordination server: {errorMessage}";
                gotDisconnected = true;
            });
        }
        var offer = await signalingUtils.ConstructOfferAsync(client.Thumbprint);

        if (gotDisconnected) return;
        try
        {
            await signaling.SendAsync(offer, "offer");
        }
        catch (InvalidOperationException ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LobbyText = $"Could not connect to coordination server: {ex.Message}";
            });
        }

        var answer = await signaling.AnswerTsc.Task;
        LobbyText = "Connecting to peer...";
        signalingUtils.ProcessAnswer(answer);
        if (signalingUtils.ChosenPeerIp == null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LobbyText = "Could not connect to peer: Could not agree on IP generation.";
            });
            return;
        }
        await client.StartAsync(
            signalingUtils.ChosenPeerIp,
            signalingUtils.ChosenPeerPort,
            signalingUtils.IsIpv6,
            signalingUtils.ChosenOwnPort,
            signalingUtils.ServerThumbprint!);
        
        await Dispatcher.UIThread.InvokeAsync(() => { State = AppState.InRoom; });
        
    }

    [RelayCommand]
    private async Task CreateRoom()
    {
        server = new Server();
        await Dispatcher.UIThread.InvokeAsync(() => LobbyText = "Connecting to coordination server...");

        await using WebSocketSignaling signaling = new WebSocketSignaling(WsBaseUri);
        
        signaling.OnDisconnected += (_, description) =>
        {
            if (server.IsClientConnected) return;
            Dispatcher.UIThread.Post(() =>
            {
                State = AppState.Lobby;
                LobbyText = $"Disconnected from coordination server: {(string.IsNullOrEmpty(description) ?
                    "The signaling was closed before your peer could join." : description)}";
            });
        };
        var (success, errorMessage) = await signaling.ConnectAsync(Role.Server, RoomCode);
        if (success is not true)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                State = AppState.Lobby;
                LobbyText = $"Could not connect to coordination server: {errorMessage}";
            });
        }        
        var info = await signaling.RoomInfoTcs.Task;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            State = AppState.WaitingForConnection;
            RoomCode = info.id;
        });

        Console.WriteLine(RoomCode);
        
        var offer = await signaling.OfferTcs.Task;
        var answer = await signalingUtils.ConstructAnswerAsync(offer, server.Thumbprint);
        await server.StartAsync(signalingUtils.IsIpv6, signalingUtils.ChosenOwnPort,
            signalingUtils.ClientThumbprint!);
        await server.StartAsync(signalingUtils.IsIpv6, signalingUtils.ChosenOwnPort,
            signalingUtils.ClientThumbprint!);
        try
        {
            await signaling.SendAsync(answer, "answer");
        }
        catch (InvalidOperationException ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                State = AppState.Lobby;
                LobbyText = $"Could not connect to coordination server: {ex.Message}";
            });
        }

        await server.ClientConnected.Task;
    }
}
