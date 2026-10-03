using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

public sealed partial class PokemonEditorView : UserControl
{
    public PokemonEditorView()
    {
        InitializeComponent();
        // O Click nao traz as teclas modificadoras: guarda as do momento em que o mouse foi pressionado (os botoes
        // marcam o PointerPressed como tratado, por isso handledEventsToo).
        foreach (var b in new[] { ShinyStar, ShinyButton })
            b.AddHandler(PointerPressedEvent, (_, e) => _shinyMods = e.KeyModifiers, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private KeyModifiers _shinyMods;

    /// <summary>Simbolo do genero: troca macho/femea (a forma acompanha nas especies em que a forma e o genero).</summary>
    private void OnGenderClick(object? sender, RoutedEventArgs e) => VM?.ToggleGender();

    /// <summary>Estrela / "Tornar shiny": Alt mantem o PID (troca o SID), Shift = quadrado, Ctrl = estrela.</summary>
    private void OnShinyClick(object? sender, RoutedEventArgs e)
    {
        var mods = _shinyMods;
        _shinyMods = KeyModifiers.None; // Enter/Espaco depois vale como clique simples
        VM?.ShinyClick(mods.HasFlag(KeyModifiers.Alt), mods.HasFlag(KeyModifiers.Shift), mods.HasFlag(KeyModifiers.Control));
    }

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
