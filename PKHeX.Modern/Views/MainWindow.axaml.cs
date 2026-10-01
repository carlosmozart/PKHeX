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

    /// <summary>
    /// Atalhos globais. Teclas sem modificador (Q/E/Esc) sao ignoradas enquanto o foco esta num campo
    /// de texto ou lista, para nao atrapalhar a digitacao. Ctrl+Z/Y ficam em Window.KeyBindings.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not MainViewModel vm)
            return;

        var mods = e.KeyModifiers;
        if (mods == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.O: OnOpen(this, e); e.Handled = true; return;
                case Key.S or Key.E when vm.HasSave: OnExport(this, e); e.Handled = true; return;
                case >= Key.D1 and <= Key.D9: vm.GoToPage(e.Key - Key.D1); e.Handled = true; return;
                case >= Key.NumPad1 and <= Key.NumPad9: vm.GoToPage(e.Key - Key.NumPad1); e.Handled = true; return;
            }
            return;
        }
        if (mods != KeyModifiers.None || IsTyping())
            return;
        switch (e.Key)
        {
            case Key.Q: vm.CyclePage(-1); e.Handled = true; break;
            case Key.E: vm.CyclePage(+1); e.Handled = true; break;
            case Key.Escape: vm.Back(); e.Handled = true; break;
        }
    }

    private bool IsTyping() => FocusManager?.GetFocusedElement() is TextBox or ComboBox or NumericUpDown or CalendarDatePicker or AutoCompleteBox;

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
