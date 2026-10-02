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

    /// <summary>Restaurar como...: escolhe o arquivo de destino (sugere a pasta do save de origem e o nome original).</summary>
    private async void OnRestoreAs(object? sender, RoutedEventArgs e)
    {
        if (VM is not { } vm || (sender as Control)?.DataContext is not BackupEntryViewModel backup
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var start = backup.Source is { } src && System.IO.Path.GetDirectoryName(src) is { } dir && System.IO.Directory.Exists(dir)
            ? await storage.TryGetFolderFromPathAsync(dir) : null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Restaurar backup como",
            SuggestedFileName = backup.SaveName,
            SuggestedStartLocation = start,
        });
        if (file?.TryGetLocalPath() is { } path)
            await vm.RestoreToAsync(backup, path);
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
