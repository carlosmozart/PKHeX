using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        _drag = new SlotDragController(this, () => VM);
    }

    private readonly SlotDragController _drag;

    private MainViewModel VM => (MainViewModel)DataContext!;

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir save",
            AllowMultiple = false,
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            VM.Open(path);
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar save",
            SuggestedFileName = VM.SuggestedFileName,
        });
        if (file?.TryGetLocalPath() is { } path)
            VM.Export(path);
    }

    private async void OnImportEntity(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar Pokémon",
            AllowMultiple = false,
            FileTypeFilter = [EntityFileType, FilePickerFileTypes.All],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            VM.ImportFile(path);
    }

    private async void OnExportEntity(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar Pokémon",
            SuggestedFileName = VM.SuggestedEntityFileName,
            FileTypeChoices = [EntityFileType],
        });
        if (file?.TryGetLocalPath() is { } path)
            VM.ExportEntity(path);
    }

    private FilePickerFileType EntityFileType => new("Pokémon")
    {
        Patterns = [.. VM.EntityExtensions.Select(x => $"*.{x}")],
    };
}
