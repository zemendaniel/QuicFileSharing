﻿using CommunityToolkit.Mvvm.ComponentModel;

namespace QuicFileSharing.GUI.ViewModels;

public class ViewModelBase : ObservableObject
{
    protected static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
