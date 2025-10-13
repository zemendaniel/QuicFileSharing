using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using QuicFileSharing.GUI.ViewModels;

namespace QuicFileSharing.GUI.Views;

public partial class FileOfferDialog : Window
{
    public FileOfferDialog()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (DataContext is FileOfferDialogViewModel vm)
                _ = HandleResultAsync(vm);
        };
    }

    private async Task HandleResultAsync(FileOfferDialogViewModel vm)
    {
        var (accepted, path) = await vm.ResultTask;
        Close((accepted, path));
    }
}