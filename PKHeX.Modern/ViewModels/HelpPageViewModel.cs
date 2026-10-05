using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Pagina Ajuda (F1): funcoes do app (<see cref="HelpContent"/>), novidades (CHANGELOG.md embutido)
/// e Sobre (versao, verificacao de releases novas, AllGenWiki). Funciona com ou sem save aberto.
/// </summary>
public sealed class HelpPageViewModel : PageViewModel
{
    private readonly AppSettings _settings;

    public HelpPageViewModel(AppSettings settings)
    {
        _settings = settings;
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckUpdatesAsync(silent: false), () => !IsChecking);
        OpenLatestCommand = new RelayCommand(() => _ = InstallUpdateAsync(), () => !IsReviewingUpdate && !IsDownloading);
        InstallUpdateCommand = new RelayCommand(() => _ = InstallUpdateAsync(), () => HasUpdate && !IsDownloading && !IsReadyToRestart && !IsReviewingUpdate);
        RestartCommand = new RelayCommand(() => RestartRequested?.Invoke());
        OpenReleasesCommand = new RelayCommand(() => Links.Open(UpdateChecker.ReleasesUrl));
        OpenRepoCommand = new RelayCommand(() => Links.Open(UpdateChecker.RepoUrl));
        OpenWikiCommand = new RelayCommand(() => Links.Open(Links.AllGenWiki));
        HideOverlayCommand = new RelayCommand(() => { OverlayHidden = true; _demoCts?.Cancel(); });
        DemoAnimationCommand = new RelayCommand(() => _ = DemoAsync(), () => !IsDownloading && !IsDemo);
        DiagnosticCommand = new RelayCommand(() => _ = ShareDiagnosticAsync(), () => !IsPreparingDiagnostic);
        ApplyFilter();
    }

    public override string Title => "Ajuda";
    public override string Icon => "❔";
    public override void Load(SaveFile sav) { }

    /// <summary>0 = Funções, 1 = Novidades, 2 = Sobre.</summary>
    public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }
    private int _selectedTab;

    // Funcoes
    private string _filter = "";
    /// <summary>Filtra as funcoes por titulo ou texto (sem diferenciar maiusculas e acentos).</summary>
    public string Filter { get => _filter; set { if (Set(ref _filter, value ?? "")) ApplyFilter(); } }
    public IReadOnlyList<HelpSection> Sections { get; private set; } = [];
    public bool HasNoResults => Sections.Count == 0;

    private void ApplyFilter()
    {
        var f = Normalize(_filter.Trim());
        // Atalhos de teclado so no desktop; a secao Android so no Android.
        var all = HelpContent.Sections.Where(s => s.Title != (App.ShowShortcuts ? "Android" : "Atalhos")).ToArray();
        Sections = f.Length == 0
            ? all
            : [.. all
                .Select(s => Normalize($"{s.Title} {Loc.T(s.Title)}").Contains(f) ? s : s with { Items = [.. s.Items.Where(i => Normalize($"{i.Title} {i.Text} {i.Shortcut} {Loc.T(i.Title)} {Loc.T(i.Text)}").Contains(f))] })
                .Where(s => s.Items.Count > 0)];
        Raise(nameof(Sections));
        Raise(nameof(HasNoResults));
    }

    private static string Normalize(string s)
    {
        var d = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string([.. d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)]);
    }

    // Novidades
    public IReadOnlyList<ChangelogVersion> Changelog => Services.Changelog.Versions;
    /// <summary>Em outro idioma, avisa que o changelog continua em portugues (ele nao e traduzido).</summary>
    public bool ShowChangelogLanguageNote => Loc.IsTranslating;

    // Sobre
    public string VersionText => $"Versão {UpdateChecker.CurrentText}";
    public RelayCommand DiagnosticCommand { get; }
    public string DiagnosticButtonText => App.ShowShortcuts ? "📋 Copiar diagnóstico" : "📋 Compartilhar diagnóstico";
    public Func<string>? PrepareDiagnostic { get; set; }
    public Func<string, Task<bool>>? ConfirmDiagnostic { get; set; }
    public Func<string, Task<bool>>? SendDiagnostic { get; set; }
    public Action<string>? DiagnosticStatus { get; set; }
    private bool _preparingDiagnostic;
    public bool IsPreparingDiagnostic { get => _preparingDiagnostic; private set { Set(ref _preparingDiagnostic, value); DiagnosticCommand.NotifyCanExecuteChanged(); } }

    public async Task ShareDiagnosticAsync()
    {
        if (IsPreparingDiagnostic) return;
        IsPreparingDiagnostic = true;
        try
        {
            var text = await Task.Run(() => PrepareDiagnostic?.Invoke() ?? DiagnosticReport.Build(_settings));
            if (ConfirmDiagnostic is null || !await ConfirmDiagnostic(text)) return;
            var sent = SendDiagnostic is not null && await SendDiagnostic(text);
            DiagnosticStatus?.Invoke(sent ? "Diagnóstico preparado para compartilhar." : "Não foi possível compartilhar o diagnóstico.");
        }
        catch (Exception ex) { DiagnosticStatus?.Invoke("Diagnóstico: " + ex.Message); }
        finally { IsPreparingDiagnostic = false; }
    }
    public RelayCommand OpenReleasesCommand { get; }
    public RelayCommand OpenRepoCommand { get; }
    public RelayCommand OpenWikiCommand { get; }
    public string WikiUrl => Links.AllGenWiki;

    /// <summary>Verificar se saiu versao nova ao iniciar o app (preferencia salva).</summary>
    public bool CheckOnStartup
    {
        get => _settings.CheckForUpdates;
        set { _settings.CheckForUpdates = value; _settings.Save(); Raise(); }
    }

    /// <summary>Oferecer a janela de novidades ao achar uma versao nova (preferencia salva).</summary>
    public bool AutoUpdate
    {
        get => _settings.AutoUpdate;
        set { _settings.AutoUpdate = value; _settings.Save(); Raise(); }
    }
    /// <summary>Esta copia pode se atualizar sozinha (exe publicado). Rodando pelo codigo, so mostra o link.</summary>
    public bool CanSelfUpdate => AutoUpdater.CanSelfUpdate;
    public Func<ReleaseInfo, Task<bool>>? ReviewUpdate { get; set; }
    private bool _isReviewingUpdate;
    public bool IsReviewingUpdate { get => _isReviewingUpdate; private set { Set(ref _isReviewingUpdate, value); RaiseUpdateState(); } }

    // Atualizacoes
    public RelayCommand InstallUpdateCommand { get; }
    public RelayCommand RestartCommand { get; }
    /// <summary>A janela fecha o app e abre o exe novo (perguntando antes se houver alteracoes nao salvas).</summary>
    public Action? RestartRequested { get; set; }

    private bool _isDownloading;
    public bool IsDownloading { get => _isDownloading; private set { Set(ref _isDownloading, value); RaiseUpdateState(); } }
    private double _downloadProgress;
    /// <summary>0-100.</summary>
    public double DownloadProgress { get => _downloadProgress; private set { Set(ref _downloadProgress, value); Raise(nameof(DownloadText)); Raise(nameof(AnimationProgress)); Raise(nameof(OverlayText)); } }

    // Painel da animacao (Pokebolas ganhando cor e a equipe andando) por cima do app enquanto baixa
    public RelayCommand HideOverlayCommand { get; }
    public RelayCommand DemoAnimationCommand { get; }
    private bool _overlayHidden;
    /// <summary>"Continuar usando": esconde o painel; o download segue e o progresso continua na barra lateral.</summary>
    public bool OverlayHidden { get => _overlayHidden; private set { Set(ref _overlayHidden, value); RaiseOverlay(); } }
    private bool _isDemo;
    public bool IsDemo { get => _isDemo; private set { Set(ref _isDemo, value); RaiseOverlay(); DemoAnimationCommand.NotifyCanExecuteChanged(); } }
    private double _demoProgress;
    private System.Threading.CancellationTokenSource? _demoCts;
    public bool ShowUpdateOverlay => !OverlayHidden && (IsDownloading || IsDemo || IsReadyToRestart);
    public double AnimationProgress => IsDemo ? _demoProgress : IsReadyToRestart ? 100 : DownloadProgress;
    public bool AnimationDone => AnimationProgress >= 100;
    public string OverlayText => IsDemo
        ? (_demoProgress >= 100 ? "Pronto! (demonstração: nada foi baixado)" : $"Demonstração da atualização... {_demoProgress:0}%")
        : IsReadyToRestart ? $"Pronto! A versão {(Latest is { } l ? UpdateChecker.Format(l.Version) : "")} está instalada." : DownloadText;
    public string OverlayHint => IsReadyToRestart && !IsDemo
        ? "Reinicie para usar a versão nova (o app pergunta antes se houver alterações não salvas)."
        : "Pode continuar usando o app: o download segue em segundo plano.";

    private void RaiseOverlay()
    {
        foreach (var p in (string[])[nameof(ShowUpdateOverlay), nameof(AnimationProgress), nameof(AnimationDone), nameof(OverlayText), nameof(OverlayHint)])
            Raise(p);
    }

    /// <summary>Roda a animacao com um progresso falso (Ajuda › Sobre), para ver sem precisar de uma versao nova.</summary>
    private async Task DemoAsync()
    {
        _demoCts = new System.Threading.CancellationTokenSource();
        var token = _demoCts.Token;
        _demoProgress = 0;
        OverlayHidden = false;
        IsDemo = true;
        try
        {
            while (_demoProgress < 100 && !token.IsCancellationRequested)
            {
                await Task.Delay(120);
                _demoProgress = Math.Min(100, _demoProgress + 1 + Random.Shared.NextDouble() * 2);
                RaiseOverlay();
            }
            if (!token.IsCancellationRequested)
                await Task.Delay(3500, token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsDemo = false;
            OverlayHidden = false;
        }
    }
    public string DownloadText => Latest is { } l ? $"Baixando a {UpdateChecker.Format(l.Version)}... {DownloadProgress:0}%" : "";
    private bool _isReadyToRestart;
    /// <summary>A versao nova ja esta instalada; vale ao reiniciar.</summary>
    public bool IsReadyToRestart { get => _isReadyToRestart; private set { Set(ref _isReadyToRestart, value); RaiseUpdateState(); } }
    public string RestartText => Latest is { } l ? $"🔄  Reiniciar na versão {UpdateChecker.Format(l.Version)}" : "";
    /// <summary>Aviso "nova versao" na barra lateral: so quando ainda nao esta baixando nem instalada.</summary>
    public bool ShowUpdateBanner => HasUpdate && !IsDownloading && !IsReadyToRestart;
    public bool ShowInstallButton => ShowUpdateBanner && CanSelfUpdate;
    /// <summary>Rodando pelo codigo: o aviso so leva ao site.</summary>
    public bool ShowLinkBanner => ShowUpdateBanner && !CanSelfUpdate;

    private void RaiseUpdateState()
    {
        foreach (var p in (string[])[nameof(ShowUpdateBanner), nameof(ShowInstallButton), nameof(ShowLinkBanner), nameof(RestartText), nameof(DownloadText)])
            Raise(p);
        InstallUpdateCommand.NotifyCanExecuteChanged();
        OpenLatestCommand.NotifyCanExecuteChanged();
        DemoAnimationCommand?.NotifyCanExecuteChanged();
        RaiseOverlay();
    }

    /// <summary>Baixa e instala a release mais nova. Se falhar, o aviso continua com o link para baixar manualmente.</summary>
    public async Task InstallUpdateAsync()
    {
        if (Latest is not { } release || !HasUpdate || IsDownloading || IsReadyToRestart || IsReviewingUpdate)
            return;
        if (ReviewUpdate is null) return;
        IsReviewingUpdate = true;
        bool accepted;
        try { accepted = await ReviewUpdate(release); }
        finally { IsReviewingUpdate = false; }
        if (!accepted || Latest != release) return;
        if (!CanSelfUpdate)
        {
            Links.Open(release.Url);
            return;
        }
        _demoCts?.Cancel();
        OverlayHidden = false;
        IsDownloading = true;
        DownloadProgress = 0;
        try
        {
            var progress = new Progress<double>(v => DownloadProgress = v * 100);
            await AutoUpdater.InstallAsync(release, progress);
            if (AutoUpdater.InstallsByHandoff)
            {
                // Android: o instalador do sistema assume daqui; o aviso volta se a instalacao for cancelada.
                UpdateText = $"Instalador do Android aberto para a versão {UpdateChecker.Format(release.Version)}. Confirme a instalação; se o Android pedir, permita instalar apps do PKHeX Modern.";
                return;
            }
            IsReadyToRestart = true;
            UpdateText = $"Versão {UpdateChecker.Format(release.Version)} instalada. Reinicie o app para usar (seu trabalho não é perdido sem perguntar).";
        }
        catch (Exception ex)
        {
            UpdateText = $"Não foi possível atualizar: {ex.Message}. Consulte a página de releases para baixar pelo site.";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    public RelayCommand CheckUpdatesCommand { get; }
    public RelayCommand OpenLatestCommand { get; }
    public ReleaseInfo? Latest { get; private set; }
    /// <summary>Ha uma release mais nova que este executavel (aviso na barra lateral).</summary>
    public bool HasUpdate => Latest is { } l && l.Version > UpdateChecker.Current;
    public string UpdateBanner => Latest is { } l ? $"⬆  Nova versão {UpdateChecker.Format(l.Version)}" : "";
    public string LatestButtonText => Latest is { } l ? $"⬇  Baixar a versão {UpdateChecker.Format(l.Version)}" : "";
    private string _updateText = "Ainda não verificado.";
    public string UpdateText { get => _updateText; private set => Set(ref _updateText, value); }
    private bool _isChecking;
    public bool IsChecking { get => _isChecking; private set { Set(ref _isChecking, value); CheckUpdatesCommand.NotifyCanExecuteChanged(); } }

    /// <summary>Consulta as releases do GitHub. <paramref name="silent"/>: ao iniciar, falha de rede nao vira mensagem de erro.</summary>
    public async Task CheckUpdatesAsync(bool silent)
    {
        if (IsChecking)
            return;
        IsChecking = true;
        UpdateText = "Verificando...";
        try
        {
            SetLatest(await UpdateChecker.GetLatestAsync());
        }
        catch (Exception ex)
        {
            UpdateText = silent ? "Não foi possível verificar agora (sem conexão?)." : $"Não foi possível verificar: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
        if (HasUpdate && AutoUpdate && CanSelfUpdate)
            await InstallUpdateAsync();
    }

    /// <summary>Aplica o resultado da consulta (separado para os testes).</summary>
    public void SetLatest(ReleaseInfo? latest)
    {
        Latest = latest;
        var when = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        UpdateText = latest is null
            ? $"Nenhuma release encontrada (verificado em {when})."
            : HasUpdate
                ? $"Saiu a versão {UpdateChecker.Format(latest.Version)}{(latest.Published is { } pub ? $", publicada em {pub.LocalDateTime:dd/MM/yyyy}" : "")}. Você está na {UpdateChecker.CurrentText}."
                : $"Você está na versão mais recente ({UpdateChecker.CurrentText}). Verificado em {when}.";
        foreach (var p in (string[])[nameof(Latest), nameof(HasUpdate), nameof(UpdateBanner), nameof(LatestButtonText)])
            Raise(p);
        RaiseUpdateState();
    }
}
