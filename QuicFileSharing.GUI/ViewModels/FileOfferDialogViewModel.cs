using System.Runtime.InteropServices;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using System;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace QuicFileSharing.GUI.ViewModels;

public partial class FileOfferDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string message = string.Empty;

    private TaskCompletionSource<(bool accepted, string? path)> tcs = new();

    public Task<(bool accepted, string? path)> ResultTask => tcs.Task;

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

        if (folders.Count == 0)
        {
            tcs.SetResult((false, null));
            return;
        }
        
        var folderPath = ResolveFolderPath(folders[0]);
        Console.WriteLine("path in accept:");
        Console.WriteLine(folderPath);
        tcs.SetResult((true, folderPath));
        
    }

    [RelayCommand]
    private void Reject()
    {
        tcs.SetResult((false, null));
    }
    
    private static string ResolveFolderPath(IStorageFolder folder)
    {
        if (folder.Path is not { IsAbsoluteUri: true, Scheme: "file" })
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        
        var path = Uri.UnescapeDataString(folder.Path.LocalPath);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return path;
        
        path = path.Replace('/', '\\');
        if (folder.Path.Host != "")
            path = $@"\\{folder.Path.Host}{path[1..]}";

        return path;
    }

}