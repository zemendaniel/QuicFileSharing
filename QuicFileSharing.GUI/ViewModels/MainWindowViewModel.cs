using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuicFileSharing.Core;
using QuicFileSharing.GUI.Models;

namespace QuicFileSharing.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private Core.QuicFileSharing service = new();
    private const string WsBaseUri = "ws://vps.zemendaniel.hu:8080";
    
    [ObservableProperty]
    private AppState state = AppState.Lobby;
    
    [ObservableProperty]
    private string roomCode = string.Empty;
    
    [ObservableProperty]
    private string roomLoadingMessage = string.Empty;
    
    [ObservableProperty]
    private string statusMessage = string.Empty;

    public MainWindowViewModel()
    {
        service.RoomCreated += id =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                RoomCode = id;
                
            });
        };
        service.ConnectionReady += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                State = AppState.InRoom;
            });
        };
    }
    
    [RelayCommand]
    private async Task JoinRoom()
    {
        await service.Start(Role.Client, WsBaseUri, RoomCode);
        State = AppState.Loading;
        RoomLoadingMessage = "Joining room...";
    }
    
    [RelayCommand]
    private async Task CreateRoom()
    {
        await service.Start(Role.Server, WsBaseUri);
        State = AppState.Loading;
        RoomLoadingMessage = "Waiting for connection...";
    }
}
