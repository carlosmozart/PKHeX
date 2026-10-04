using System;
using System.IO;
using System.Linq;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.Views;

public sealed partial class MainView
{
    private async void OnExportBoxFolder(object? sender, RoutedEventArgs e)
    {
        BoxFolderButton.Flyout?.Hide();
        var temp = Path.Combine(Path.GetTempPath(), "PKHeX.Modern", "box-export", Guid.NewGuid().ToString("N"));
        try
        {
            using var folder = (await Storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.T("Exportar caixas para pasta..."), AllowMultiple = false })).FirstOrDefault();
            if (folder is null) return;
            int count;
            if (folder.TryGetLocalPath() is { } path) count = VM.ExportBoxesToFolder(path);
            else { VM.ExportBoxesToFolder(temp); count = await BoxFolderTransfer.WriteAsync(temp, folder); }
            VM.Status = $"Arquivos exportados: {count}.";
        }
        catch (Exception ex) { await ShowErrorAsync("Não foi possível exportar as caixas", ex); }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    private async void OnImportBoxFolder(object? sender, RoutedEventArgs e)
    {
        BoxFolderButton.Flyout?.Hide();
        var temp = Path.Combine(Path.GetTempPath(), "PKHeX.Modern", "box-read", Guid.NewGuid().ToString("N"));
        try
        {
            using var folder = (await Storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.T("Importar pasta para as caixas..."), AllowMultiple = false })).FirstOrDefault();
            if (folder is null) return;
            var files = folder.TryGetLocalPath() is { } path ? Directory.GetFiles(path, "*", SearchOption.AllDirectories) : await BoxFolderTransfer.ReadAsync(folder, temp);
            await VM.ImportBoxFolderAsync(files);
        }
        catch (Exception ex) { await ShowErrorAsync("Não foi possível importar a pasta", ex); }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
