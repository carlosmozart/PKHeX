using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

/// <summary>
/// Vista unica do Android: a mesma <see cref="MainView"/> do desktop, em paisagem, reduzida quando a tela tem
/// menos espaco que o tamanho minimo da janela do desktop (telefones), para caber sem cortar a barra lateral e o editor.
/// </summary>
public sealed class MobileShell : UserControl
{
    /// <summary>Area minima (em pixels logicos) que a interface do desktop precisa para nao apertar.</summary>
    public const double DesignWidth = 1180, DesignHeight = 620;

    private readonly ScaleTransform _scale = new();
    public MainView View { get; }

    public MobileShell(MainViewModel vm)
    {
        View = new MainView { DataContext = vm };
        Content = new LayoutTransformControl { LayoutTransform = _scale, Child = View };
        SizeChanged += (_, e) => Fit(e.NewSize);
        Loaded += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not { } top)
                return;
            top.BackRequested += (_, e) => { e.Handled = true; Back(vm); };
            // Links (releases, wiki) pelo Android: Process.Start nao existe la.
            Services.Links.Opener = url => { _ = top.Launcher.LaunchUriAsync(new Uri(url)); return true; };
            // Como o MainWindow.Opened do desktop: limpa o APK de uma atualizacao anterior e verifica a release nova.
            Services.AutoUpdater.CleanupOld();
            // Traz o que mudou nas pastas externas do Bank desde a ultima abertura.
            // Traz os saves novos ou mudados da pasta de saves.
            _ = vm.SaveManager.RefreshAsync();
            if (MainView.BankFolders is { } folders)
                _ = SyncBankAsync(folders, top.StorageProvider, vm);
            if (vm.Settings is { Persist: true, CheckForUpdates: true })
                _ = vm.Help.CheckUpdatesAsync(silent: true);
        };
    }

    private static async System.Threading.Tasks.Task SyncBankAsync(Services.MobileBankFolders folders, Avalonia.Platform.Storage.IStorageProvider storage, MainViewModel vm)
    {
        await folders.SyncAllAsync(storage);
        vm.Bank.Reload();
    }

    /// <summary>Escala usada para uma tela de <paramref name="size"/> (1 em tablets e telas grandes).</summary>
    public static double ScaleFor(Size size) =>
        size.Width <= 0 || size.Height <= 0 ? 1 : Math.Min(1, Math.Min(size.Width / DesignWidth, size.Height / DesignHeight));

    private void Fit(Size size)
    {
        var s = ScaleFor(size);
        _scale.ScaleX = _scale.ScaleY = s;
    }

    /// <summary>Botao Voltar do Android: fecha a pergunta aberta ou volta como o Esc do desktop.</summary>
    private static void Back(MainViewModel vm)
    {
        if (vm.Dialog is { } dialog)
            dialog.Complete(false);
        else
            vm.Back();
    }
}
