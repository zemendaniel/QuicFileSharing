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
    private WebSocketSignaling signaling;
    

    public MainWindowViewModel()
    {
        
    }

    [RelayCommand]
    private async Task JoinRoom()
    {
        client = new Client();
        using (signaling = new(WsBaseUri))
        {

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

            signaling.OnMessageReceived += async message =>
            {
                var msg = JsonSerializer.Deserialize<SignalingMessage>(message, SignalingUtils.Options);
                if (msg == null) return;

                switch (msg.Type)
                {
                    case "answer":
                        LobbyText = "Connecting to peer...";
                        signalingUtils.ProcessAnswer(msg.Data);

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
                        break;
                }
            };

            LobbyText = "Connecting to coordination server...";
            var (success, errorMessage) = await signaling.ConnectAsync(Role.Client, RoomCode);
            if (success is not true)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LobbyText = $"Could not connect to coordination server: {errorMessage}";
                });
                return;
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
        }
    }

    [RelayCommand]
    private async Task CreateRoom()
    {
        server = new Server();
        using (signaling = new(WsBaseUri))
        {

            signaling.OnDisconnected += (reason, description) =>
            {
                if (server.IsClientConnected) return;
                Dispatcher.UIThread.Post(() =>
                {
                    State = AppState.Lobby;
                    LobbyText = $"Disconnected from coordination server: {(string.IsNullOrEmpty(description) ?
                        "The signaling was closed before your peer could join." : description)}";
                });
            };

            signaling.OnMessageReceived += async message =>
            {
                var msg = JsonSerializer.Deserialize<SignalingMessage>(message, SignalingUtils.Options);
                if (msg == null) return;
                switch (msg.Type)
                {
                    case "room_info":
                        State = AppState.WaitingForConnection;
                        var info = JsonSerializer.Deserialize<RoomInfo>(msg.Data);
                        if (info is null) return;
                        RoomCode = info.id;
                        break;
                    case "offer":
                        var answer = await signalingUtils.ConstructAnswerAsync(msg.Data, server.Thumbprint);
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

                        break;
                }
            };
            LobbyText = "Connecting to coordination server...";

            var (success, errorMessage) = await signaling.ConnectAsync(Role.Server, RoomCode);
            if (success is not true)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    State = AppState.Lobby;
                    LobbyText = $"Could not connect to coordination server: {errorMessage}";
                });
            }
        }
    }
}
