using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        _drag = new SlotDragController(this, () => VM);
        // Ctrl+Tab / Ctrl+Shift+Tab trocam de aba de save (em tunel: o Tab normal e consumido pela navegacao de foco).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Control) && DataContext is MainViewModel { Dialog: null } vm)
            {
                vm.CycleTab(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : +1);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Como no TidalHeX: ao voltar para a janela, a lista de saves e relida (novos arquivos aparecem sozinhos).
        Activated += (_, _) => { if (DataContext is MainViewModel vm && (!vm.HasSave || vm.CurrentPage == vm.SaveManager)) _ = vm.SaveManager.RefreshAsync(); };
        Closing += OnClosing;
        // Verifica se saiu release nova (so no app de verdade: testes usam preferencias em memoria).
        Opened += (_, _) =>
        {
            if (DataContext is not MainViewModel { Settings.Persist: true } vm)
                return;
            // Primeira abertura depois de uma atualizacao: apaga o exe antigo e avisa.
            if (AutoUpdater.CleanupOld())
                vm.Status = $"PKHeX Modern atualizado para a versão {UpdateChecker.CurrentText}. Veja o que mudou em Ajuda › Novidades (F1).";
            if (vm.Settings.CheckForUpdates)
                _ = vm.Help.CheckUpdatesAsync(silent: true);
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm)
                return;
            vm.SaveRequested += () => OnExport(this, new RoutedEventArgs());
            vm.Help.RestartRequested = () => _ = RestartForUpdateAsync(vm);
            // Seletores de arquivo/pasta usados pela pagina Bank (pasta externa e outro save).
            vm.Bank.PickFolder = async () => (await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Pasta com arquivos .pk* para usar como banco",
                AllowMultiple = false,
            })).FirstOrDefault()?.TryGetLocalPath();
            vm.OtherSave.PickFile = async () => (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Abrir outro save",
                AllowMultiple = false,
            })).FirstOrDefault()?.TryGetLocalPath();
        };
    }

    private bool _closeConfirmed;

    /// <summary>Fechar com alteracoes nao exportadas: pergunta dentro da janela antes de sair.</summary>
    /// <summary>Reiniciar na versao nova: pergunta se houver alteracoes nao salvas, abre o exe novo (com o save aberto) e fecha este.</summary>
    private async Task RestartForUpdateAsync(MainViewModel vm)
    {
        if ((vm.IsDirty || vm.OtherSave.IsDirty) && !await vm.ConfirmCloseAsync())
            return;
        if (!AutoUpdater.Restart(vm.HasSave ? vm.Settings.LastSavePath : null))
        {
            vm.Status = "Não foi possível reiniciar sozinho. Feche e abra o PKHeX Modern para usar a versão nova.";
            return;
        }
        _closeConfirmed = true;
        Close();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainViewModel vm || (!vm.IsDirty && !vm.OtherSave.IsDirty))
            return;
        e.Cancel = true;
        if (await vm.ConfirmCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
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

        // Pergunta aberta: Enter confirma, Esc cancela, o resto fica bloqueado.
        if (vm.Dialog is { } dialog)
        {
            if (e.Key is Key.Enter or Key.Return)
                dialog.Complete(true);
            else if (e.Key == Key.Escape)
                dialog.Complete(false);
            e.Handled = true;
            return;
        }

        var mods = e.KeyModifiers;
        if (e.Key == Key.F1 && mods == KeyModifiers.None)
        {
            vm.OpenHelp();
            e.Handled = true;
            return;
        }
        if (mods == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.O: OnOpen(this, e); e.Handled = true; return;
                case Key.W when vm.ActiveTab is { } tab: _ = vm.CloseTabAsync(tab); e.Handled = true; return;
                case Key.F when vm.HasSave: this.FindControl<TextBox>("SearchBox")?.Focus(); e.Handled = true; return;
                case Key.S or Key.E when vm.HasSave: OnExport(this, e); e.Handled = true; return;
                case Key.A when vm.HasSave && !IsTyping(): vm.MarkAll(); e.Handled = true; return;
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
            case Key.Delete when vm.DeleteCommand.CanExecute(null): vm.DeleteCommand.Execute(null); e.Handled = true; break;
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
            await VM.OpenAsync(path);
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        // Save que veio de um .zip: gravar de volta na mesma entrada (com backup do zip) ou exportar como arquivo.
        if (VM.ZipSavePath is { } zipPath)
        {
            if (await VM.ConfirmAsync("Salvar save do .zip",
                    $"Este save está dentro de {ZipSaves.DisplayName(zipPath)}. Gravar de volta no zip (o zip inteiro ganha um backup antes) ou salvar como um arquivo separado?",
                    "Gravar dentro do .zip", "Salvar como arquivo...", icon: "🗜"))
            {
                VM.Export(zipPath);
                return;
            }
        }
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
            await VM.ImportFileAsync(path);
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
