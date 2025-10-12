using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using System;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace QuicFileSharing.GUI.ViewModels;

public partial class FileOfferDialogViewModel : ObservableObject
{
    public string Message { get; }

    private TaskCompletionSource<(bool accepted, Uri? path)> tcs = new();

    public Task<(bool accepted, Uri? path)> ResultTask => tcs.Task;

    public FileOfferDialogViewModel(string fileName, long fileSize)
    {
        Message = $"Incoming file: {fileName} ({fileSize / 1024.0 / 1024.0:F2} MB)";
    }

    [RelayCommand]
    private async Task Accept(Window window)
    {
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder to save the file"
        });
        if (folders.Count > 0)
            tcs.SetResult((true, folders[0].Path));
        else
            tcs.SetResult((false, null));
    }

    [RelayCommand]
    private void Reject()
    {
        tcs.SetResult((false, null));
    }
}