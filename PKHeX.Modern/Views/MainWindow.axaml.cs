using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

/// <summary>Janela do desktop: hospeda <see cref="MainView"/> e cuida de fechar, reiniciar e verificar atualizacoes.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
            vm.Help.RestartRequested = () => _ = RestartForUpdateAsync(vm);
            vm.RestartAppRequested = () => RestartAsync(vm, "Reinicie manualmente",
                "O PKHeX Modern não conseguiu se abrir de novo sozinho. Feche o app e abra outra vez para usar o novo idioma.");
        };
    }

    private bool _closeConfirmed;

    /// <summary>Reiniciar na versao nova: pergunta se houver alteracoes nao salvas, abre o exe novo (com o save aberto) e fecha este.</summary>
    private Task RestartForUpdateAsync(MainViewModel vm) => RestartAsync(vm, "Reinicie manualmente",
        "A versão nova já está instalada, mas o PKHeX Modern não conseguiu se abrir de novo sozinho. Feche o app e abra o PKHeX.Modern.exe outra vez para usar a versão nova.");

    /// <summary>Reinicia o app (pergunta se houver alteracoes nao salvas e reabre o save); se nao der, explica com <paramref name="failMessage"/>.</summary>
    private async Task RestartAsync(MainViewModel vm, string failTitle, string failMessage)
    {
        if ((vm.IsDirty || vm.OtherSave.IsDirty) && !await vm.ConfirmCloseAsync())
            return;
        bool restarted;
        try
        {
            restarted = AutoUpdater.Restart(vm.HasSave ? vm.Settings.LastSavePath : null);
        }
        catch (System.Exception ex)
        {
            Services.CrashLog.Write(ex);
            restarted = false;
        }
        if (!restarted)
        {
            await vm.ConfirmAsync(failTitle, failMessage, "OK", cancelText: "", icon: "🔄");
            return;
        }
        _closeConfirmed = true;
        Close();
    }

    /// <summary>Fechar com alteracoes nao exportadas: pergunta dentro da janela antes de sair.</summary>
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

    /// <summary>Teclas sem nada focado chegam na janela: repassa para os atalhos da MainView.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
            View.HandleKey(e);
    }
}
