using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

/// <summary>
/// Interface principal (barra lateral, paginas, editor, barra de acoes). No desktop fica dentro de <see cref="MainWindow"/>;
/// no Android e a vista unica do app.
/// </summary>
public sealed partial class MainView : UserControl
{
    /// <summary>
    /// Documentos do Android (seletor SAF): arquivos content:// nao tem caminho local, entao sao copiados para uma
    /// pasta privada, editados la e gravados de volta no original ao salvar. Nulo no desktop.
    /// </summary>
    public static MobileDocuments? Documents { get; set; }

    /// <summary>Pasta de saves no Android (lida pelo seletor; os saves gravam de volta no original). Nulo no desktop.</summary>
    public static MobileSaveFolder? SaveFolder { get; set; }

    /// <summary>Pastas externas do Bank no Android (copia privada sincronizada com a pasta do seletor). Nulo no desktop.</summary>
    public static MobileBankFolders? BankFolders { get; set; }

    // Varias gravacoes seguidas (mover varios Pokemon) viram uma sincronizacao so, meio segundo depois da ultima.
    private readonly Avalonia.Threading.DispatcherTimer _bankPush = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly System.Collections.Generic.HashSet<string> _bankChanged = [];

    public MainView()
    {
        InitializeComponent();
        Controls.TouchHelp.Initialize();
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Pointer.Type != PointerType.Touch) return;
            VM.IsTouchUI = true;
            Controls.TouchHelp.ObserveTouch(this);
        }, RoutingStrategies.Tunnel);
        _drag = new SlotDragController(this, () => VM);
        if (!App.ShowShortcuts)
            SearchBox.Watermark = Loc.T("🔍  Buscar");
        // Ctrl+Tab / Ctrl+Shift+Tab trocam de aba de save (em tunel: o Tab normal e consumido pela navegacao de foco).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Control) && DataContext is MainViewModel { Dialog: null } vm)
            {
                vm.CycleTab(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : +1);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm)
                return;
            vm.SaveRequested += () => OnQuickSave(this, new RoutedEventArgs());
            vm.Help.SendDiagnostic = async text =>
            {
                if (!App.ShowShortcuts) return DiagnosticReport.ShareText?.Invoke(text) == true;
                if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return false;
                await clipboard.SetTextAsync(text);
                return true;
            };
            vm.Game.PickCardFile = PickCardFileAsync;
            vm.Game.ExportCardFile = ExportCardFileAsync;
            vm.SaveQrImage = SaveQrImageAsync;
            // Seletores de arquivo/pasta usados pela pagina Bank (pasta externa e outro save).
            if (SaveFolder is { } saveFolder)
            {
                saveFolder.IsOpen = vm.SaveManager.IsOpenPath;
                vm.SaveManager.BeforeRefresh = async () =>
                {
                    if (await saveFolder.SyncAsync(Storage) is not null)
                        vm.Status = saveFolder.LastSummary;
                };
                vm.SaveManager.DescribeFolder = () => saveFolder.FolderName is { } name ? "📁 " + name : "Nenhuma pasta escolhida: toque em “Escolher pasta...”";
            }
            if (BankFolders is { } folders)
            {
                vm.Bank.PickFolder = async () =>
                {
                    try { return await folders.AddAsync(Storage); }
                    catch (Exception ex) { await ShowErrorAsync("Não foi possível usar a pasta", ex); return null; }
                };
                vm.Bank.FolderRemoved = folders.Remove;
                vm.Bank.DescribeFolder = folders.Describe;
                BankStorage.ExternalChanged += folder => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _bankChanged.Add(folder);
                    _bankPush.Stop();
                    _bankPush.Start();
                });
                _bankPush.Tick += async (_, _) =>
                {
                    _bankPush.Stop();
                    var changed = _bankChanged.ToArray();
                    _bankChanged.Clear();
                    foreach (var folder in changed)
                        await folders.PushAsync(folder, Storage);
                };
            }
            else vm.Bank.PickFolder = async () => (await Storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Pasta com arquivos .pk* para usar como banco",
                AllowMultiple = false,
            })).FirstOrDefault()?.TryGetLocalPath();
            vm.OtherSave.PickFile = async () => await LocalPathAsync((await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Abrir outro save",
                AllowMultiple = false,
            })).FirstOrDefault());
        };
    }

    private readonly SlotDragController _drag;

    private MainViewModel VM => (MainViewModel)DataContext!;

    private IStorageProvider Storage => TopLevel.GetTopLevel(this)?.StorageProvider ?? throw new IOException("Seletor de arquivos indisponível.");

    /// <summary>Caminho local do arquivo escolhido; no Android, importa o documento para a pasta privada.</summary>
    private static async Task<string?> LocalPathAsync(IStorageFile? file)
    {
        if (file is null)
            return null;
        if (file.TryGetLocalPath() is { } path)
            return path;
        return Documents is null ? null : (await Documents.ImportAsync(file)).Path;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
            HandleKey(e);
    }

    /// <summary>
    /// Atalhos globais. Teclas sem modificador (Q/E/Esc) sao ignoradas enquanto o foco esta num campo
    /// de texto ou lista, para nao atrapalhar a digitacao. Ctrl+Z/Y ficam nos KeyBindings.
    /// </summary>
    public void HandleKey(KeyEventArgs e)
    {
        if (e.Handled || DataContext is not MainViewModel vm)
            return;

        // Pergunta aberta: Enter confirma, Esc cancela, o resto fica bloqueado.
        if (vm.HasQr && vm.Dialog is null)
        {
            if (e.Key == Key.Escape) vm.CloseQr();
            e.Handled = true; return;
        }
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
        if (mods == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.F && vm.HasSave)
        {
            vm.CurrentPage = vm.Search;
            e.Handled = true;
            return;
        }
        if (mods == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.O: OnOpen(this, e); e.Handled = true; return;
                case Key.W when vm.ActiveTab is { } tab: _ = vm.CloseTabAsync(tab); e.Handled = true; return;
                case Key.F when vm.HasSave: SearchBox.Focus(); e.Handled = true; return;
                case Key.S when vm.HasSave: OnQuickSave(this, e); e.Handled = true; return;
                case Key.E when vm.HasSave: OnExport(this, e); e.Handled = true; return;
                case Key.A when vm.HasSave && !IsTyping(): vm.MarkAll(); e.Handled = true; return;
                case Key.D0 or Key.NumPad0: vm.GoToPage(-1); e.Handled = true; return;
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

    private bool IsTyping() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or ComboBox or NumericUpDown or CalendarDatePicker or AutoCompleteBox;

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Abrir save",
                AllowMultiple = false,
            });
            if (await LocalPathAsync(files.FirstOrDefault()) is { } path)
                await VM.OpenAsync(path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Não foi possível abrir", ex);
        }
    }

    /// <summary>Salvar: grava por cima do arquivo do save sem abrir janela; sem arquivo de origem, cai no Salvar como.</summary>
    private async void OnQuickSave(object? sender, RoutedEventArgs e)
    {
        if (!VM.CanQuickSave)
        {
            OnExport(sender, e);
            return;
        }
        var path = VM.QuickSavePath!;
        if (!VM.QuickSave())
        {
            if (VM.SaveError is { } error)
                await VM.ConfirmAsync("Não foi possível salvar", error, "OK", cancelText: "", icon: "⚠");
            return;
        }
        // Android: a copia privada foi gravada; agora vai para o arquivo original escolhido no seletor.
        if (Documents?.Find(path) is { } doc)
        {
            try
            {
                await Documents.SaveAsync(doc, await File.ReadAllBytesAsync(ZipSaves.FileOf(path)), Storage);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Não foi possível gravar no arquivo original", ex);
            }
        }
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
                if (!VM.Export(zipPath) && VM.SaveError is { } zipError)
                    await VM.ConfirmAsync("Não foi possível salvar", zipError, "OK", cancelText: "", icon: "⚠");
                return;
            }
        }
        try
        {
            var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exportar save",
                SuggestedFileName = VM.SuggestedFileName,
            });
            await WriteAsync(file, path => VM.Export(path), "Não foi possível salvar");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Não foi possível salvar", ex);
        }
    }

    /// <summary>Grava pelo caminho local; sem caminho (Android), grava num temporario e copia para o documento escolhido.</summary>
    private async Task WriteAsync(IStorageFile? file, Func<string, bool> write, string errorTitle)
    {
        if (file is null)
            return;
        if (file.TryGetLocalPath() is { } path)
        {
            if (!write(path) && VM.SaveError is { } error)
                await VM.ConfirmAsync(errorTitle, error, "OK", cancelText: "", icon: "⚠");
            return;
        }
        var temp = Path.Combine(Path.GetTempPath(), "pkhex-" + Guid.NewGuid().ToString("N") + Path.GetExtension(file.Name));
        try
        {
            if (!write(temp))
            {
                if (VM.SaveError is { } error)
                    await VM.ConfirmAsync(errorTitle, error, "OK", cancelText: "", icon: "⚠");
                return;
            }
            await MobileDocuments.WriteVerifiedAsync(file, await File.ReadAllBytesAsync(temp));
            VM.Status = "Salvo: " + file.Name;
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private Task ShowErrorAsync(string title, Exception ex)
    {
        CrashLog.Write(ex);
        return VM.ConfirmAsync(title, ex.Message, "OK", cancelText: "", icon: "⚠");
    }

    private async void OnImportEntity(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importar Pokémon",
                AllowMultiple = false,
                FileTypeFilter = [EntityFileType, new FilePickerFileType("Mystery Gift") { Patterns = ["*.wc*", "*.pgf", "*.pcd", "*.pgt", "*.wb*", "*.wa*", "*.wr7"] }, FilePickerFileTypes.All],
            });
            if (files.FirstOrDefault() is not { } file)
                return;
            if (file.TryGetLocalPath() is { } path)
            {
                await VM.ImportFileAsync(path);
                return;
            }
            // Android: copia o arquivo para um temporario com o mesmo nome (a extensao diz o formato).
            var folder = Path.Combine(Path.GetTempPath(), "pkhex-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var temp = Path.Combine(folder, Path.GetFileName(file.Name));
            try
            {
                await using (var input = await file.OpenReadAsync())
                    await File.WriteAllBytesAsync(temp, await MobileDocuments.ReadBoundedAsync(input));
                await VM.ImportFileAsync(temp);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Não foi possível importar", ex);
        }
    }

    private async void OnExportEntity(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exportar Pokémon",
                SuggestedFileName = VM.SuggestedEntityFileName,
                FileTypeChoices = [EntityFileType],
            });
            await WriteAsync(file, path => { VM.ExportEntity(path); return File.Exists(path); }, "Não foi possível exportar");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Não foi possível exportar", ex);
        }
    }

    private FilePickerFileType EntityFileType => new("Pokémon")
    {
        Patterns = [.. VM.EntityExtensions.Select(x => $"*.{x}")],
    };
}
