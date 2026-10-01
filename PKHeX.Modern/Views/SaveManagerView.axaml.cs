using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

public sealed partial class SaveManagerView : UserControl
{
    public SaveManagerView() => InitializeComponent();

    private SaveManagerViewModel? VM => DataContext as SaveManagerViewModel;

    /// <summary>Duplo clique no cartao abre o save.</summary>
    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SaveEntryViewModel entry)
            VM?.OpenCommand.Execute(entry);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.F5)
        {
            VM?.RefreshCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (VM is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta de saves", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            vm.Folder = path;
    }
}
