using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

public sealed partial class PokemonEditorView : UserControl
{
    public PokemonEditorView() => InitializeComponent();

    private PokemonEditorViewModel? VM => DataContext as PokemonEditorViewModel;

    private async void OnExportShowdown(object? sender, RoutedEventArgs e)
    {
        if (VM is not { } vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clip)
            return;
        await clip.SetTextAsync(vm.ExportShowdown());
    }

    private async void OnImportShowdown(object? sender, RoutedEventArgs e)
    {
        if (VM is not { } vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clip)
            return;
        vm.ImportShowdown(await clip.TryGetTextAsync());
    }
}
