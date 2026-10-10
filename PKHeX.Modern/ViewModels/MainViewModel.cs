using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private SaveFile? _sav;
    private SlotHistory? _history;
    private SlotViewModel? _selectedSlot;

    public AppSettings Settings { get; }

    public MainViewModel(AppSettings? settings = null)
    {
        Settings = settings ?? new AppSettings();
        OpenLastCommand = new RelayCommand(() => { if (HasLastSave) _ = OpenAsync(Settings.LastSavePath!); });
        // So a interface e em portugues; nomes do jogo (especies, golpes, itens) seguem o ingles do PKHeX.
        CoreAdapter.SetLanguage("en");
        var accent = Theme.AccentTheme.Find(Settings.AccentColor);
        AccentOptions = [.. Theme.AccentTheme.Presets.Select(p => new AccentOptionViewModel(p) { IsSelected = p == accent })];
        SetAccentCommand = new RelayCommand(p => { if (p is AccentOptionViewModel o) SetAccent(o.Preset); });
        var theme = Theme.AppTheme.Find(Settings.ThemeKey);
        ThemeOptions = [.. Theme.AppTheme.Presets.Select(t => new ThemeOptionViewModel(t, t == theme, new RelayCommand(() => SetTheme(t))))];
        var font = Theme.AppTheme.FindFont(Settings.FontKey);
        FontOptions = [.. Theme.AppTheme.Fonts.Select(f => new FontOptionViewModel(f, f == font, new RelayCommand(() => SetFont(f))))];
        LanguageOptions = [.. Loc.Languages.Select(l => new LanguageOptionViewModel(l.Code, l.Name, l.Code == (Settings.UiLanguage ?? Loc.Portuguese),
            new RelayCommand(() => _ = SetLanguageAsync(l.Code, l.Name))))];
        ToggleThemeCommand = new RelayCommand(() =>
        {
            Settings.DarkTheme = App.ToggleTheme();
            Settings.Save();
            foreach (var option in ThemeOptions) option.RefreshPreview();
        });
        Party = new PartyPageViewModel(s => _ = SelectSlotAsync(s));
        Party.EditDaycare = EditDaycareAsync;
        Party.DepositDaycare = DepositDaycareAsync;
        Party.WithdrawDaycare = WithdrawDaycareAsync;
        Party.DaycareStatus = s => Status = s;
        Boxes = new BoxesPageViewModel(s => _ = SelectSlotAsync(s)) { Party = Party };
        Boxes.WallpaperIntensity = WallpaperIntensityIndex;
        Theme.SpriteScale.Apply(Settings.SpriteSize);
        // Registro de paginas: a ordem aqui e a ordem na barra lateral.
        SaveManager = new SaveManagerViewModel(Settings, p => _ = OpenAsync(p), (t, m, ok) => ConfirmAsync(t, m, ok, isDanger: true), s => Status = s)
        {
            IsOpenPath = p => FindTab(p) is not null,
            Reload = p => _ = ReloadAsync(p),
        };
        SaveManager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SaveManagerViewModel.Entries)) { Search?.InvalidateSources(); Pokedex?.LivingDex?.InvalidateSources(); }
            if (e.PropertyName == nameof(SaveManagerViewModel.HasEntries))
                Raise(nameof(ShowSidebarSaves));
        };
        BankStorage.ExternalFolders = Settings.ExternalBankFolders;
        OtherSave = new OtherSaveViewModel(s => _ = SelectSlotAsync(s),
            () => SaveManager.Entries
                .Where(e => _sav?.Metadata.FilePath is not { } open || !string.Equals(System.IO.Path.GetFullPath(e.Path), System.IO.Path.GetFullPath(open), StringComparison.OrdinalIgnoreCase))
                .Select(e => new OtherSaveOption(e.Path, $"{e.Game} · {e.Entry.Trainer} — {e.FileName}")),
            (t, m, ok) => ConfirmAsync(t, m, ok, isDanger: true), s => Status = s);
        Bank = new BankPageViewModel(s => _ = SelectSlotAsync(s), PromptAsync,
            (t, m, ok) => ConfirmAsync(t, m, ok, isDanger: true), s => Status = s, Settings, OtherSave);
        Pokedex = new PokedexPageViewModel(Settings, (box, slot) => _ = GoToSlotAsync(box, slot),
            (t, m, ok, details) => ConfirmAsync(t, m, ok, details: details, icon: "📖"), s => Status = s);
        Search = new SearchPageViewModel(Settings,
            () => [.. OpenSaves.Select(t => (t.Path, t == _activeTab && _sav is not null ? _sav : t.Sav))], OpenSearchResultAsync);
        Search.ShowComparison = details => ConfirmAsync("Comparar anexados", "Confira as diferenças entre os dados vinculados.", "OK", cancelText: "", details: details);
        Search.IsPageActive = () => CurrentPage == Search;
        Encounters = new EncounterDbViewModel(UseEncounter);
        Gifts = new GiftDbViewModel(UseEncounter);
        Pokedex.LivingDex = new LivingDexViewModel(Settings,
            () => [.. OpenSaves.Select(t => (t.Path, t == _activeTab && _sav is not null ? _sav : t.Sav))],
            () => _sav, OpenSearchResultAsync, (species, version) =>
            {
                // A pagina Encontros pertence ao save aberto ("Usar" grava nele): nao troca o save dela pelo jogo alvo.
                // "So deste jogo" so fica ligado quando o alvo e o proprio jogo do save; senao a busca mostra todos os jogos.
                var sameGame = _sav is { } sav && (sav.Version == version || GameUtil.GetVersionsInGeneration(sav.Context, sav.Version).Contains(version));
                Encounters.OnlyThisGame = sameGame;
                Encounters.Species = CoreAdapter.SpeciesNames[species];
                CurrentPage = Encounters;
                _ = Encounters.SearchAsync();
                if (!sameGame)
                    Status = $"Encontros de {CoreAdapter.SpeciesNames[species]} em todos os jogos (o alvo da Living Dex, {CoreAdapter.GetVersionName(version)}, não é o jogo deste save).";
            });
        Help = new HelpPageViewModel(Settings);
        Pokedex.LivingDex.LegalMode = () => LegalMode;
        Pokedex.LivingDex.ApplyPlan = ApplyLivingDexAsync;
        Help.PrepareDiagnostic = () => DiagnosticReport.Build(Settings, Views.MainView.SaveFolder?.LastCounts);
        Help.ConfirmDiagnostic = text => ConfirmAsync("Compartilhar diagnóstico", "Confira o texto antes de compartilhar. Mensagens livres e dados de saves são omitidos para proteger sua privacidade.",
            App.ShowShortcuts ? "Copiar" : "Compartilhar", details: text.Split(Environment.NewLine), icon: "📋");
        Help.DiagnosticStatus = s => Status = s;
        // Animacao da atualizacao: a equipe do save aberto anda em fila sobre a barra.
        Controls.UpdateAnimation.Walkers = () => _sav is not { } sav ? []
            : [.. Enumerable.Range(0, sav.PartyCount).Select(sav.GetPartySlotAtIndex).Where(p => p.Species != 0 && !p.IsEgg).Select(p => (p.Species, p.IsShiny))];
        Help.ReviewUpdate = release => Dialog is not null ? Task.FromResult(false) : ConfirmAsync(
            $"Nova versão {UpdateChecker.Format(release.Version)}",
            Help.CanSelfUpdate ? $"Você está na {UpdateChecker.CurrentText}. Confira as novidades antes de baixar e instalar. A versão nova vale ao reiniciar."
                : $"Você está na {UpdateChecker.CurrentText}. Confira as novidades antes de abrir a página de download.",
            Help.CanSelfUpdate ? "Atualizar agora" : "Abrir download", "Depois",
            details: string.IsNullOrWhiteSpace(release.Notes)
                ? ["Esta versão não tem notas publicadas. Consulte a página da release para mais detalhes."]
                : [.. release.Notes.Replace("\r", "").Split('\n').Select(line => line.Trim().TrimStart('#').Trim().Replace("**", "")).Where(line => line.Length > 0)],
            icon: "⬆");
        OpenHelpCommand = new RelayCommand(OpenHelp);
        CloseHelpCommand = new RelayCommand(() => IsHelpOpen = false);
        Home = new HomePageViewModel(page => CurrentPage = page,
            i => { CurrentPage = Party; if (i < Party.Slots.Count) _ = SelectSlotAsync(Party.Slots[i]); },
            p => _ = OpenAsync(p), () => Settings.RecentSaves, () => _activeTab is { } t ? FullPath(t.Path) : null) { Pages = () => Pages };
        Batch = new BatchPageViewModel(GetBatchTargets, () => _sav, ApplyBatchAsync);
        Game = new GamePageViewModel((t, m, ok) => ConfirmAsync(t, m, ok, isDanger: true, icon: "🎮"), s => Status = s);
        Game.IsLegalMode = () => LegalMode;
        Game.ShowCardQr = ShowCardQr;
        AddSelectedCardCommand = new RelayCommand(() =>
        {
            if (Gifts.Selected?.Encounter is DataMysteryGift gift && Game.AddCard(gift)) CurrentPage = Game;
        }, () => Game.HasCards && Gifts.Selected?.Encounter is DataMysteryGift);
        Gifts.PropertyChanged += (_, _) => AddSelectedCardCommand.NotifyCanExecuteChanged();
        Game.PropertyChanged += (_, _) => AddSelectedCardCommand.NotifyCanExecuteChanged();
        var bag = new BagPageViewModel(s => Status = s);
        AllPages = [Boxes, Party, Bank, Pokedex, new TrainerPageViewModel(), bag, Encounters, Gifts, SaveManager, Game, Search, Batch];
        // Atalhos da pagina Jogo podem dar itens (Member Card, Colorful Screws...) e liberar a Pokedex Nacional
        Game.BeforeShortcut = ApplyPendingPages;
        Game.AfterShortcut = () =>
        {
            if (_sav is null)
                return;
            bag.Load(_sav);
            Pokedex.Load(_sav);
        };
        foreach (var page in AllPages)
            page.Changed = () => IsDirty = true;
        Game.HistoryChanged = OnHistoryChanged;
        Boxes.SlotsLoaded = () => { ApplySearchHighlight(); ApplyMarks(); };
        Bank.SlotsLoaded = ApplyMarks;
        OtherSave.SlotsLoaded = ApplyMarks;
        Bank.Sorted = ClearMarks;
        Bank.UseVariant = UseVariantAsync;
        Boxes.Sort = SortBoxes;
        Boxes.Prompt = PromptAsync;
        ClearMarksCommand = new RelayCommand(ClearMarks);
        InitializeTouchCommands();
        InitializeShowdownTeam();
        Boxes.ExportReportCommand = new RelayCommand(() => _ = ExportBoxReportAsync(false));
        Bank.ExportReportCommand = new RelayCommand(() => _ = ExportBoxReportAsync(true));
        DeleteMarkedCommand = new RelayCommand(() => _ = DeleteMarkedAsync());
        Party.SlotsLoaded = ApplySearchHighlight;
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        GoToSearchHitCommand = new RelayCommand(p => { if (p is SearchHitViewModel h) _ = GoToSearchHitAsync(h); });
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RunSearch(); };
        _currentPage = Boxes;
        CheckLegalityCommand = new RelayCommand(() => _ = CheckLegalityAsync(), () => HasSave && !_checkingLegality);
        CreateCommand = new RelayCommand(CreateInFirstEmpty, () => HasSave);
        DeleteCommand = new RelayCommand(() => _ = DeleteSelectedAsync(), () => CanExportEntity || HasMarks);
        UndoCommand = new RelayCommand(Undo, () => CurrentPage == Game ? Game.History?.CanUndo == true : _history?.CanUndo == true);
        ShowPendingCommand = new RelayCommand(() => _ = ShowPendingAsync());
        RedoCommand = new RelayCommand(Redo, () => CurrentPage == Game ? Game.History?.CanRedo == true : _history?.CanRedo == true);
    }

    public RelayCommand CheckLegalityCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RedoCommand { get; }
    public string UndoTip => (CurrentPage == Game ? Game.History?.UndoDescription : _history?.UndoDescription) is { } d ? $"Desfazer: {d} (Ctrl+Z)" : "Nada para desfazer (Ctrl+Z)";
    public string RedoTip => (CurrentPage == Game ? Game.History?.RedoDescription : _history?.RedoDescription) is { } d ? $"Refazer: {d} (Ctrl+Y)" : "Nada para refazer (Ctrl+Y)";
    public RelayCommand CreateCommand { get; }

    /// <summary>Slot selecionado com Pokemon (para Exportar PKM).</summary>
    public bool CanExportEntity => _selectedSlot is { IsEmpty: false };
    public string? SuggestedEntityFileName => CanExportEntity ? CoreAdapter.GetEntityFileName(_selectedSlot!.Pkm!) : null;
    public IReadOnlyList<string> EntityExtensions => _sav is null ? [] : CoreAdapter.GetEntityExtensions(_sav);

    public BoxesPageViewModel Boxes { get; }
    public SaveManagerViewModel SaveManager { get; }
    /// <summary>Bank local (pagina com duas telas: bank | save).</summary>
    public BankPageViewModel Bank { get; }
    /// <summary>Segundo save (painel "Outro save" da pagina Bank).</summary>
    public OtherSaveViewModel OtherSave { get; }
    /// <summary>Pokedex centralizada (saves da pasta + save aberto + bank).</summary>
    public PokedexPageViewModel Pokedex { get; }
    public EncounterDbViewModel Encounters { get; }
    /// <summary>Pesquisa: todos os Pokemon dos saves abertos, da pasta e do bank, com filtros.</summary>
    public SearchPageViewModel Search { get; }
    public BatchPageViewModel Batch { get; }
    public GamePageViewModel Game { get; }
    public RelayCommand AddSelectedCardCommand { get; }
    public GiftDbViewModel Gifts { get; }
    /// <summary>Ajuda (F1): funcoes, novidades, Sobre e verificacao de atualizacoes.</summary>
    public HelpPageViewModel Help { get; }
    public RelayCommand OpenHelpCommand { get; }
    public RelayCommand CloseHelpCommand { get; }

    private bool _isHelpOpen;
    /// <summary>Ajuda aberta: ocupa a area de conteudo (no lugar da pagina ou do Save Manager) ate Voltar/Esc ou trocar de pagina.</summary>
    public bool IsHelpOpen
    {
        get => _isHelpOpen;
        set
        {
            if (Set(ref _isHelpOpen, value))
                RaiseHome();
        }
    }
    public bool ShowHomeSaves => !HasSave && !IsHelpOpen;
    public bool ShowPage => HasSave && !IsHelpOpen;
    private void RaiseHome()
    {
        Raise(nameof(ShowHomeSaves));
        Raise(nameof(ShowPage));
        Raise(nameof(ShowEditorPanel));
        RaiseActionBar();
    }

    /// <summary>F1 / botao "Ajuda e novidades".</summary>
    public void OpenHelp() => IsHelpOpen = true;
    public PartyPageViewModel Party { get; }
    /// <summary>Tela inicial do save aberto (Ctrl+0); fica fora da lista numerada da barra lateral.</summary>
    public HomePageViewModel Home { get; }
    public RelayCommand GoHomeCommand => _goHome ??= new RelayCommand(() => { if (HasSave) CurrentPage = Home; });
    private RelayCommand? _goHome;
    public bool IsHomeActive => CurrentPage == Home && !IsHelpOpen;
    private IReadOnlyList<PageViewModel> AllPages { get; }
    public IReadOnlyList<PageViewModel> Pages
    {
        get
        {
            var pages = AllPages.Where(p => p.IsAvailable).ToList();
            for (int i = 0; i < pages.Count; i++)
                pages[i].Shortcut = i < 9 ? $"Ctrl+{i + 1}" : "";
            Search.Shortcut = "Ctrl+Shift+F";
            return pages;
        }
    }

    // Atalhos de teclado (ligados em MainWindow.OnKeyDown)
    /// <summary>Vai para a pagina de indice <paramref name="index"/> (Ctrl+1..9).</summary>
    public void GoToPage(int index)
    {
        if (index < 0)
        {
            GoHomeCommand.Execute(null); // Ctrl+0
            return;
        }
        var pages = Pages;
        if (HasSave && (uint)index < (uint)pages.Count)
            CurrentPage = pages[index];
    }

    /// <summary>Pagina anterior/seguinte, circular (Q/E, como os botoes L/R).</summary>
    public void CyclePage(int delta)
    {
        var pages = Pages;
        if (!HasSave || pages.Count == 0)
            return;
        int i = Math.Max(0, pages.ToList().IndexOf(CurrentPage));
        CurrentPage = pages[(i + delta + pages.Count) % pages.Count];
    }

    /// <summary>Esc: fecha o editor; sem editor, volta para Caixas.</summary>
    public async void Back()
    {
        if (IsHelpOpen)
        {
            IsHelpOpen = false;
            return;
        }
        if (HasMarks)
        {
            ClearMarks();
            return;
        }
        if (Editor is not null)
        {
            if (!await ConfirmDiscardEditAsync())
                return;
            Editor = null;
            if (_selectedSlot is not null)
                _selectedSlot.IsSelected = false;
            _selectedSlot = null;
            RaiseSelectionChanged();
            return;
        }
        if (HasSave)
            CurrentPage = Boxes;
    }

    private PageViewModel _currentPage;
    public PageViewModel CurrentPage
    {
        get => _currentPage;
        set
        {
            if (value is null)
                return;
            IsHelpOpen = false;
            if (!Set(ref _currentPage, value))
                return;
            Raise(nameof(ShowEditorPanel));
            Raise(nameof(ShowTouchToolbar));
            Raise(nameof(SelectionBoxOptions));
            Raise(nameof(IsHomeActive));
            RaiseActionBar();
            OnHistoryChanged();
            if (value == Home)
                Home.Refresh(); // a equipe e os numeros podem ter mudado
            else if (value == SaveManager)
                _ = SaveManager.RefreshAsync();
            else if (value == Gifts)
                _ = Gifts.EnsureLoadedAsync();
            else if (value == Pokedex)
            {
                if (Pokedex.ShowLivingDex && Pokedex.LivingDex is not null) { if (Pokedex.LivingDex.Plan is null) _ = Pokedex.LivingDex.RefreshAsync(); } // o plano e pesado: refaz pelo "Gerar plano"
                else _ = Pokedex.RefreshAsync();
            }
            else if (value == Search)
                _ = Search.RefreshAsync();
        }
    }

    public RelayCommand ToggleThemeCommand { get; }

    // Cor de destaque
    public IReadOnlyList<AccentOptionViewModel> AccentOptions { get; }

    /// <summary>Temas completos (menu "🎨 Tema" da barra lateral).</summary>
    public IReadOnlyList<ThemeOptionViewModel> ThemeOptions { get; }
    public string ThemeText => "🎨  " + Loc.T(Theme.AppTheme.Current.ShortName);

    /// <summary>Troca o tema na hora; a cor de destaque passa a ser a sugerida pelo tema (da para trocar depois).</summary>
    private void SetTheme(Theme.AppThemePreset theme)
    {
        Theme.AppTheme.Apply(theme);
        foreach (var t in ThemeOptions)
            t.IsSelected = t.Preset == theme;
        Settings.ThemeKey = theme.Key;
        foreach (var f in FontOptions)
            f.RefreshFamily();
        SetAccent(Theme.AccentTheme.Find(theme.AccentKey)); // tambem salva as preferencias
        Raise(nameof(ThemeText));
        Status = $"Tema {theme.Name} aplicado.";
    }

    /// <summary>Fontes da interface (⚙ › Fonte): a do tema, Inter, Pixelify Sans...</summary>
    public IReadOnlyList<FontOptionViewModel> FontOptions { get; }

    /// <summary>Troca a fonte na hora, independente do tema.</summary>
    private void SetFont(Theme.AppFontChoice font)
    {
        Theme.AppTheme.SetFont(font.Key);
        foreach (var f in FontOptions)
            f.IsSelected = f.Font == font;
        Settings.FontKey = font.Key == "theme" ? null : font.Key;
        Settings.Save();
        Status = $"Fonte: {Loc.T(font.Name)}.";
    }

    /// <summary>Idiomas da interface (menu "🌐 Idioma" da barra lateral).</summary>
    public IReadOnlyList<LanguageOptionViewModel> LanguageOptions { get; }
    /// <summary>Botao compacto da barra lateral: "🌐 PT" / "🌐 EN".</summary>
    public string LanguageShort => "🌐 " + ((Settings.UiLanguage ?? Loc.Portuguese) == Loc.English ? "EN" : "PT");
    public string LanguageText => "🌐  " + Loc.T("Idioma") + ": " + (LanguageOptions.FirstOrDefault(l => l.IsSelected)?.Code == Loc.English ? "English" : "Português");
    /// <summary>Reiniciar o app (definido pela janela; o mesmo fluxo do Reiniciar da atualizacao).</summary>
    public Func<Task>? RestartAppRequested { get; set; }

    /// <summary>Troca o idioma da interface: salva a preferencia e oferece reiniciar (a traducao e instalada na partida).</summary>
    private async Task SetLanguageAsync(string code, string name)
    {
        if (code == (Settings.UiLanguage ?? Loc.Portuguese))
            return;
        Settings.UiLanguage = code;
        Settings.Save();
        foreach (var l in LanguageOptions)
            l.IsSelected = l.Code == code;
        Raise(nameof(LanguageText));
        Raise(nameof(LanguageShort));
        // A pergunta aparece nos dois idiomas: a tela atual ainda esta no idioma antigo.
        var restart = code == Loc.English
            ? await ConfirmAsync("Restart to switch to English?", "The interface language changes when the app restarts. Game names (species, moves, items) stay in English.\n\nA interface muda de idioma ao reiniciar o app.", "Restart now", cancelText: "Later", icon: "🌐")
            : await ConfirmAsync("Reiniciar para usar Português?", "O idioma da interface muda ao reiniciar o app. Nomes do jogo (espécies, golpes, itens) continuam em inglês.\n\nThe interface language changes when the app restarts.", "Reiniciar agora", cancelText: "Depois", icon: "🌐");
        if (restart && RestartAppRequested is { } run)
            await run();
        else
            Status = code == Loc.English ? "Language set to English: it takes effect the next time you open the app." : "Idioma definido para Português: vale na próxima vez que abrir o app.";
    }
    public RelayCommand SetAccentCommand { get; }

    private void SetAccent(Theme.AccentPreset preset)
    {
        Theme.AccentTheme.Apply(preset);
        foreach (var o in AccentOptions)
            o.IsSelected = o.Preset == preset;
        Settings.AccentColor = preset.Key;
        Settings.Save();
    }
    public RelayCommand OpenLastCommand { get; }

    public bool HasLastSave => ZipSaves.Exists(Settings.LastSavePath);
    /// <summary>Link "↺ Último" na barra lateral: so sem save aberto (com save, as abas e o Inicio mostram os recentes).</summary>
    public bool ShowLastSaveLink => HasLastSave && !HasSave;
    /// <summary>Sem save aberto e com saves na pasta: a barra lateral lista os saves para abrir com um clique.</summary>
    public bool ShowSidebarSaves => !HasSave && SaveManager.HasEntries;
    public string LastSaveName => HasLastSave ? ZipSaves.DisplayName(Settings.LastSavePath!) : "";

    public bool AskLegalizeOnClick
    {
        get => Settings.AskLegalizeOnClick;
        set { Settings.AskLegalizeOnClick = value; Settings.Save(); Raise(); }
    }

    public bool OpenLastSaveOnStartup
    {
        get => Settings.OpenLastSaveOnStartup;
        set { Settings.OpenLastSaveOnStartup = value; Settings.Save(); Raise(); }
    }

    /// <summary>Modo legal (padrao ligado): o editor so oferece opcoes legais e nao aplica Pokemon ilegal.</summary>
    public bool LegalMode
    {
        get => Settings.LegalMode;
        set
        {
            if (value == Settings.LegalMode)
                return;
            Settings.LegalMode = value;
            Settings.Save();
            Raise();
            if (Editor is not null)
                Editor.LegalMode = value;
            Status = value
                ? "Modo legal ligado: o editor só mostra opções legais e não aplica Pokémon ilegal."
                : "Modo legal desligado: o editor permite qualquer valor (use com cuidado).";
        }
    }

    public bool HasSave => _sav is not null;
    /// <summary>Caminho do save aberto (ou "arquivo.zip|entrada"), para os vinculos do bank.</summary>
    private string CurrentSavePath => _sav?.Metadata.FilePath ?? Settings.LastSavePath ?? "";
    private string SaveLabel => _sav is null ? "" : $"{CoreAdapter.GetGameName(_sav)} · {_sav.OT}";

    public RelayCommand SyncAttachedCommand => _syncAttached ??= new RelayCommand(() => _ = SyncAttachedAsync());
    private RelayCommand? _syncAttached;

    /// <summary>
    /// Atualizar anexados: cada Pokemon anexado e procurado no save ligado a ele (o aberto, como esta na memoria, ou o
    /// arquivo) e a versao do jogo volta para o bank. Os que sumiram (solto, trocado, save apagado) podem ser desanexados.
    /// </summary>
    private async Task SyncAttachedAsync()
    {
        if (BankLinks.All.Count == 0)
        {
            Status = "Nenhum Pokémon anexado. Ligue “Anexar ao trazer” no bank e leve um Pokémon para o save.";
            return;
        }
        Status = "Atualizando anexados...";
        var openPath = _sav?.Metadata.FilePath;
        var sav = _sav;
        IReadOnlyList<BankLinkResult> results;
        try
        {
            results = await Task.Run(() => BankLinks.Sync(openPath, sav));
        }
        catch (Exception ex)
        {
            Status = $"Erro ao atualizar anexados: {ex.Message}";
            return;
        }
        Bank.LoadBox();
        var lost = results.Where(r => r.Lost).ToList();
        var details = results.Select(r => $"{(r.Lost ? "⚠" : "✓")} {r.Link.Name} ({r.Link.SaveName}): {r.Message}").ToList();
        int updated = results.Count(r => !r.Lost);
        Status = $"Anexados: {updated} de {results.Count} atualizados" + (lost.Count > 0 ? $", {lost.Count} não encontrados." : ".");
        if (lost.Count == 0)
        {
            await ConfirmAsync("Anexados atualizados", "A versão de cada Pokémon no jogo voltou para o bank.", "OK", cancelText: "", details: details, icon: "🔗");
            return;
        }
        if (await ConfirmAsync("Anexados atualizados",
                $"{lost.Count} anexado(s) não foram encontrados (o Pokémon saiu do bank, foi solto ou trocado, ou o save mudou de lugar). Desanexar esses? Os arquivos do bank não são apagados.",
                "Desanexar os perdidos", cancelText: "Manter", details: details, icon: "🔗"))
        {
            foreach (var r in lost)
                BankLinks.Detach(r.Link.Id);
            Bank.LoadBox();
            Status += $" {lost.Count} desanexado(s).";
        }
    }
    /// <summary>Painel do editor: some nas paginas de lista (Saves, Encontros, Eventos), que usam a largura toda.</summary>
    public bool ShowEditorPanel => HasSave && CurrentPage != Home && CurrentPage != SaveManager && CurrentPage != Encounters && CurrentPage != Gifts && CurrentPage != Bank && CurrentPage != Pokedex && CurrentPage != Search && CurrentPage != Batch && CurrentPage is not (BagPageViewModel or TrainerPageViewModel or GamePageViewModel) && !IsHelpOpen;
    public bool ShowSlotActions => HasSave && !IsHelpOpen && (CurrentPage == Boxes || CurrentPage == Party);
    public bool ShowGameHistoryActions => HasSave && !IsHelpOpen && CurrentPage == Game;
    public bool ShowSaveActions => HasSave && !IsHelpOpen;
    public bool ShowSaveManagerActions => !IsHelpOpen && (!HasSave || CurrentPage == SaveManager);
    public bool ShowEncounterActions => HasSave && !IsHelpOpen && CurrentPage == Encounters;
    public bool ShowGiftActions => HasSave && !IsHelpOpen && CurrentPage == Gifts;
    public bool ShowBankActions => HasSave && !IsHelpOpen && CurrentPage == Bank;
    public bool ShowDexActions => HasSave && !IsHelpOpen && CurrentPage == Pokedex;
    public BagPageViewModel? ActionBag => !IsHelpOpen ? CurrentPage as BagPageViewModel : null;
    private void RaiseActionBar()
    {
        foreach (var property in new[] { nameof(ShowGameHistoryActions), nameof(ShowBoxFolderActions), nameof(ShowSlotActions), nameof(ShowSaveActions), nameof(ShowSaveManagerActions), nameof(ShowEncounterActions), nameof(ShowGiftActions), nameof(ShowBankActions), nameof(ShowDexActions), nameof(ActionBag) }) Raise(property);
    }
    public string GameName => _sav is null ? "Nenhum save aberto" : CoreAdapter.GetGameName(_sav);
    /// <summary>Selo do jogo aberto (Pokemon da capa nas cores da versao), no cartao da barra lateral.</summary>
    public GameArt? GameArt => _sav is null ? null : GameArt.Get(_sav.Version);
    public string TrainerInfo => _sav is null ? "Arraste um arquivo ou clique em Abrir" : $"{_sav.OT} · TID {_sav.DisplayTID}";

    private string _status = "Pronto";
    public string Status { get => _status; set => Set(ref _status, value); }

    private PokemonEditorViewModel? _editor;
    public PokemonEditorViewModel? Editor { get => _editor; private set { Set(ref _editor, value); Raise(nameof(HasEditor)); } }
    public bool HasEditor => Editor is not null;

    // Busca global
    private List<StoredEntity>? _searchIndex; // copia de todos os Pokemon do save; null = reler
    private readonly Avalonia.Threading.DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };

    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand GoToSearchHitCommand { get; }

    private string _searchText = "";
    /// <summary>Texto da busca global (especie, apelido, golpe, item, "shiny", "ovo").</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value ?? ""))
                return;
            Raise(nameof(HasSearch));
            _searchTimer.Stop();
            _searchTimer.Start(); // espera a digitacao parar um pouco
        }
    }

    public bool HasSearch => SearchText.Trim().Length > 0;
    public IReadOnlyList<SearchHitViewModel> SearchResults { get; private set; } = [];
    public string SearchSummary { get; private set; } = "";

    /// <summary>Roda a busca agora (o timer chama depois que a digitacao para).</summary>
    public void RunSearch()
    {
        _searchTimer.Stop();
        var query = SearchText.Trim();
        if (_sav is null || query.Length == 0)
        {
            SearchResults = [];
            SearchSummary = "";
        }
        else
        {
            _searchIndex ??= EntitySearch.ReadAll(_sav);
            var hits = new List<SearchHitViewModel>();
            foreach (var e in _searchIndex)
            {
                if (EntitySearch.Match(e.Pkm, query) is { } reason)
                    hits.Add(new SearchHitViewModel(e, reason, e.Box < 0 ? "Equipe" : CoreAdapter.GetBoxName(_sav, e.Box)));
            }
            SearchResults = hits;
            SearchSummary = hits.Count == 0 ? "Nada encontrado." : $"{hits.Count} encontrado(s)";
        }
        Raise(nameof(SearchResults));
        Raise(nameof(SearchSummary));
        ApplySearchHighlight();
    }

    /// <summary>Destaca na caixa/equipe abertas os slots que combinam com a busca e apaga os demais.</summary>
    private void ApplySearchHighlight()
    {
        var query = SearchText.Trim();
        foreach (var s in Boxes.Slots.Concat(Party.Slots))
        {
            bool match = query.Length > 0 && s is { IsEmpty: false, Pkm: { } pk } && EntitySearch.Match(pk, query) is not null;
            s.IsMatch = match;
            s.IsDimmed = query.Length > 0 && !match;
        }
    }

    /// <summary>Os dados mudaram: a proxima busca rele o save.</summary>
    private void InvalidateSearch()
    {
        _searchIndex = null;
        Search?.InvalidateSources();
        Pokedex?.LivingDex?.InvalidateSources();
        if (HasSearch)
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    /// <summary>Vai ate um slot do save aberto (caixa -1 = equipe) e abre no editor.</summary>
    private async Task GoToSlotAsync(int box, int slot)
    {
        if (_sav is null)
            return;
        if (box < 0)
        {
            CurrentPage = Party;
            if (slot < Party.Slots.Count)
                await SelectSlotAsync(Party.Slots[slot]);
            return;
        }
        CurrentPage = Boxes;
        Boxes.CurrentBox = box;
        if (slot < Boxes.Slots.Count)
            await SelectSlotAsync(Boxes.Slots[slot]);
    }

    /// <summary>Resultado da Pesquisa: no bank, abre a caixa; num save, abre (ou ativa) a aba e vai ate o slot.</summary>
    private async Task OpenSearchResultAsync(DbEntry entry)
    {
        if (entry.Source.Bank is { } bank)
        {
            CurrentPage = Bank;
            Bank.SelectedBank = bank;
            var boxes = BankStorage.GetBoxes(bank, create: false);
            int foundBox = -1, foundSlot = -1;
            for (int b = 0; b < boxes.Count && foundBox < 0; b++)
                for (int s = 0; s < BankStorage.SlotsPerBox; s++)
                {
                    bool found = entry.Box == -2 ? BankStorage.ReadSlot(boxes[b], s) is { } pk && BankLinks.IdOf(pk) == BankLinks.IdOf(entry.Pkm)
                        : entry.EntityFile is { } file ? string.Equals(BankStorage.GetSlotFile(boxes[b], s), file, StringComparison.OrdinalIgnoreCase)
                        : b == entry.Box && s == entry.Slot;
                    if (found) { foundBox = b; foundSlot = s; break; }
                }
            if (foundBox < 0) { Status = "A origem ou o destino mudou. Refazer a prévia é necessário."; return; }
            Bank.BoxIndex = foundBox;
            if (foundSlot < Bank.Slots.Count) await SelectSlotAsync(Bank.Slots[foundSlot]);
            Status = $"{entry.Source.Name} · {entry.Where}";
            return;
        }
        if (!ZipSaves.Exists(entry.Source.Id) && FindTab(entry.Source.Id) is null)
        {
            Status = $"O save {ZipSaves.DisplayName(entry.Source.Id)} não existe mais. Atualize a pesquisa.";
            return;
        }
        await OpenAsync(entry.Source.Id);
        if (_activeTab is { } tab && StoredPokemon.SameSource(tab.Path, entry.Source.Id))
            await GoToSlotAsync(entry.Box, entry.Slot);
    }

    private async Task GoToSearchHitAsync(SearchHitViewModel hit)
    {
        if (_sav is null)
            return;
        if (hit.Entity.Box < 0)
        {
            CurrentPage = Party;
            if (hit.Entity.Slot < Party.Slots.Count)
                await SelectSlotAsync(Party.Slots[hit.Entity.Slot]);
            return;
        }
        CurrentPage = Boxes;
        Boxes.CurrentBox = hit.Entity.Box;
        if (hit.Entity.Slot < Boxes.Slots.Count)
            await SelectSlotAsync(Boxes.Slots[hit.Entity.Slot]);
    }

    // Mensagens dentro do app
    private ConfirmDialogViewModel? _dialog;
    /// <summary>Pergunta aberta no momento (sobreposicao na janela), ou null.</summary>
    public ConfirmDialogViewModel? Dialog { get => _dialog; private set { Set(ref _dialog, value); Raise(nameof(HasDialog)); } }
    public bool HasDialog => Dialog is not null;

    /// <summary>Mostra uma pergunta dentro da janela e espera a resposta (true = confirmar).</summary>
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText = "Cancelar", bool isDanger = false,
        IReadOnlyList<string>? details = null, string icon = "", bool detailsExpanded = true)
    {
        Dialog?.Complete(false); // so uma pergunta por vez
        var dialog = new ConfirmDialogViewModel(title, message, confirmText, cancelText, isDanger, details, icon) { DetailsExpanded = detailsExpanded };
        Dialog = dialog;
        try { return await dialog.Result; }
        finally { if (Dialog == dialog) Dialog = null; }
    }

    private async Task<bool> ConfirmEditorPreviewAsync(string title, string message, string confirmText, string cancelText, IReadOnlyList<string> details)
    {
        var editor = Editor;
        var sav = _sav;
        var confirmed = await ConfirmAsync(title, message, confirmText, cancelText, details: details, icon: "✨");
        return confirmed && Editor == editor && _sav == sav;
    }

    /// <summary>Pergunta com campo de texto; retorna o texto (sem espacos nas pontas) ou null se cancelar/vazio.</summary>
    public async Task<string?> PromptAsync(string title, string message, string initial)
    {
        Dialog?.Complete(false);
        var dialog = new ConfirmDialogViewModel(title, message, "OK", "Cancelar", false) { HasInput = true, Input = initial };
        Dialog = dialog;
        try
        {
            var ok = await dialog.Result;
            return ok && !string.IsNullOrWhiteSpace(dialog.Input) ? dialog.Input.Trim() : null;
        }
        finally { if (Dialog == dialog) Dialog = null; }
    }

    // Alteracoes pendentes (desde que o save foi aberto ou salvo)
    private int _historyAtSave;
    /// <summary>Alteracoes de slots ainda nao salvas, da mais antiga para a mais nova.</summary>
    public IReadOnlyList<string> PendingActions => _history is null ? [] : [.. _history.Descriptions.Skip(Math.Min(_historyAtSave, _history.Count))];
    public string PendingText => PendingActions.Count switch
    {
        0 => "● Alterações não exportadas",
        1 => "● 1 alteração não exportada",
        var n => $"● {n} alterações não exportadas",
    };
    public RelayCommand ShowPendingCommand { get; }
    /// <summary>A janela pede para salvar (abre o seletor de arquivo); a View trata.</summary>
    public event Action? SaveRequested;

    private async Task ShowPendingAsync()
    {
        var list = PendingActions.Select((d, i) => $"{i + 1}. {char.ToUpper(d[0])}{d[1..]}").ToList();
        if (list.Count == 0)
            list.Add("Alterações nas caixas, na mochila ou nos dados do treinador.");
        if (await ConfirmAsync("Alterações não exportadas",
                "Estas alterações ainda não estão no arquivo do save. Use Salvar para gravar (Ctrl+Z desfaz a última).",
                "Salvar agora", "Fechar", isDanger: true, details: list, icon: "●"))
            SaveRequested?.Invoke();
    }

    private bool _isDirty;
    /// <summary>Ha alteracoes no save que ainda nao foram exportadas.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            Set(ref _isDirty, value);
            if (_activeTab is not null)
                _activeTab.IsDirty = value;
            Raise(nameof(PendingActions));
            Raise(nameof(PendingText));
            if (value)
                InvalidateSearch(); // algo no save mudou
            else
                _historyAtSave = _history?.Count ?? 0;
        }
    }

    /// <summary>Fechar o app: pergunta se algum save aberto (ou o outro save) tem alteracoes nao gravadas.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        var dirty = OpenSaves.Where(t => t.IsDirty).ToList();
        if (dirty.Count > 0 && !await ConfirmAsync("Alterações não exportadas",
                dirty.Count == 1
                    ? $"{dirty[0].FileName} tem alterações que ainda não foram exportadas. Fechar o PKHeX Modern vai descartá-las."
                    : $"{dirty.Count} saves abertos têm alterações que ainda não foram exportadas. Fechar o PKHeX Modern vai descartá-las.",
                "Descartar alterações", "Voltar", isDanger: true, details: [.. dirty.Select(t => $"● {t.FileName} ({t.Game})")]))
            return false;
        return !OtherSave.IsDirty || await ConfirmAsync("Outro save não salvo",
            $"O outro save ({OtherSave.FileName}, na página Bank) tem alterações que ainda não foram gravadas.", "Fechar sem salvar", "Voltar", isDanger: true);
    }

    /// <summary>Confirma o descarte das alteracoes nao exportadas do save ativo (true = pode seguir).</summary>
    public async Task<bool> ConfirmDiscardChangesAsync(string action)
    {
        if (!IsDirty)
            return true;
        return await ConfirmAsync("Alterações não exportadas",
            $"O save atual tem alterações que ainda não foram exportadas. {action} vai descartá-las.",
            "Descartar alterações", "Voltar", isDanger: true);
    }

    // Abas de saves abertos
    /// <summary>Saves abertos, um por aba. So o ativo esta carregado nas paginas.</summary>
    public System.Collections.ObjectModel.ObservableCollection<SaveTabViewModel> OpenSaves { get; } = [];
    private SaveTabViewModel? _activeTab;
    public SaveTabViewModel? ActiveTab => _activeTab;
    public bool HasTabs => OpenSaves.Count > 0;

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab: proxima/anterior aba (circular).</summary>
    public void CycleTab(int delta)
    {
        if (OpenSaves.Count < 2 || _activeTab is null)
            return;
        int i = (OpenSaves.IndexOf(_activeTab) + delta + OpenSaves.Count) % OpenSaves.Count;
        _ = SwitchToAsync(OpenSaves[i]);
    }
    /// <summary>"+" das abas: vai para o Save Manager (abrir outro save cria uma aba nova).</summary>
    public RelayCommand NewTabCommand => _newTab ??= new RelayCommand(() => { IsHelpOpen = false; CurrentPage = SaveManager; });
    private RelayCommand? _newTab;

    private SaveTabViewModel? FindTab(string path)
    {
        return OpenSaves.FirstOrDefault(t => StoredPokemon.SameSource(t.Path, path));
    }

    private static string FullPath(string path) => ZipSaves.IsZipPath(path, out var zip, out var entry)
        ? ZipSaves.Combine(System.IO.Path.GetFullPath(zip), entry)
        : System.IO.Path.GetFullPath(path);

    /// <summary>Abre um save pela interface numa aba nova. Se ja estiver aberto, so troca para a aba dele.</summary>
    public async Task OpenAsync(string path)
    {
        if (FindTab(path) is { } tab)
        {
            await SwitchToAsync(tab);
            Status = $"{tab.FileName} já estava aberto (aba ativada).";
            return;
        }
        if (await ConfirmDiscardEditAsync())
            Open(path);
    }

    /// <summary>Troca de aba (pergunta antes de descartar uma edicao nao aplicada no editor).</summary>
    public async Task SwitchToAsync(SaveTabViewModel tab)
    {
        if (tab == _activeTab)
        {
            IsHelpOpen = false;
            if (CurrentPage == SaveManager)
                CurrentPage = tab.LastPage ?? Boxes;
            return;
        }
        if (!await ConfirmDiscardEditAsync())
            return;
        StoreActiveTab();
        ActivateTab(tab);
        Status = $"{tab.FileName} ({tab.Game})" + (tab.IsDirty ? " · com alterações não exportadas" : "");
    }

    /// <summary>Fecha uma aba (pergunta se houver alteracoes nao exportadas).</summary>
    public async Task CloseTabAsync(SaveTabViewModel tab)
    {
        if (tab == _activeTab && !await ConfirmDiscardEditAsync())
            return;
        bool dirty = tab == _activeTab ? IsDirty : tab.IsDirty;
        if (dirty && !await ConfirmAsync("Fechar sem exportar?",
                $"{tab.FileName} tem alterações que ainda não foram exportadas. Fechar a aba vai descartá-las.",
                "Fechar sem salvar", "Voltar", isDanger: true))
            return;
        int index = OpenSaves.IndexOf(tab);
        OpenSaves.Remove(tab);
        Raise(nameof(HasTabs));
        if (tab == _activeTab)
        {
            _activeTab = null;
            if (OpenSaves.Count > 0)
                ActivateTab(OpenSaves[Math.Min(index, OpenSaves.Count - 1)]);
            else
                CloseAll();
        }
        SaveManager.RefreshOpenMarks();
        Status = $"{tab.FileName} fechado.";
    }

    /// <summary>Nenhum save aberto: volta para a tela inicial (Save Manager).</summary>
    private void CloseAll()
    {
        _sav = null;
        _history = null;
        _isDirty = false;
        _historyAtSave = 0;
        _searchIndex = null;
        SearchText = "";
        ClearMarks();
        _selectedSlot = null;
        Editor = null;
        _currentPage = Boxes;
        foreach (var p in (string[])[nameof(IsDirty), nameof(PendingActions), nameof(PendingText), nameof(CurrentPage), nameof(HasSave), nameof(ShowLastSaveLink), nameof(ShowSidebarSaves), nameof(ShowEditorPanel), nameof(GameName), nameof(GameArt), nameof(TrainerInfo), nameof(Pages), nameof(ActiveTab)])
            Raise(p);
        OnHistoryChanged();
        RaiseHome();
        RaiseSelectionChanged();
        CheckLegalityCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Guarda na aba ativa o que e dela antes de trocar (caixa, pagina, alteracoes pendentes).</summary>
    private void StoreActiveTab()
    {
        if (_activeTab is not { } tab)
            return;
        ApplyPendingPages(); // a mochila desta aba seria relida ao voltar e perderia o que nao foi aplicado
        tab.HistoryAtSave = _historyAtSave;
        tab.IsDirty = IsDirty;
        tab.CurrentBox = Boxes.CurrentBox;
        tab.LastPage = CurrentPage == SaveManager ? tab.LastPage : CurrentPage;
        tab.IsActive = false;
    }

    /// <summary>Carrega uma aba nas paginas: ela passa a ser "o save aberto".</summary>
    private void ActivateTab(SaveTabViewModel tab)
    {
        Pokedex.LivingDex?.InvalidateSources();
        Search.InvalidateSources();
        _activeTab = tab;
        tab.IsActive = true;
        CoreAdapter.Activate(tab.Sav); // legalidade, sprites e listas do PKHeX passam a ser deste save
        _sav = tab.Sav;
        _history = tab.History;
        _isDirty = tab.IsDirty;
        _historyAtSave = tab.HistoryAtSave;
        _searchIndex = null;
        SearchText = "";
        ClearMarks();
        OtherSave.MainPath = tab.Path;
        OnHistoryChanged();
        _selectedSlot = null;
        Editor = null;
        foreach (var page in AllPages)
            page.Load(tab.Sav);
        Boxes.CurrentBox = tab.CurrentBox;
        Home.Load(tab.Sav);
        CurrentPage = tab.LastPage ?? Home; // save recem-aberto cai no Inicio
        foreach (var p in (string[])[nameof(IsDirty), nameof(PendingActions), nameof(PendingText), nameof(HasSave), nameof(ShowLastSaveLink), nameof(ShowSidebarSaves), nameof(ShowEditorPanel), nameof(GameName), nameof(GameArt), nameof(TrainerInfo), nameof(Pages), nameof(ActiveTab)])
            Raise(p);
        IsHelpOpen = false;
        RaiseHome();
        RaiseSelectionChanged();
        CheckLegalityCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// O arquivo mudou no disco (ex.: backup restaurado): se o save estiver numa aba, rele do disco
    /// (pergunta antes de descartar alteracoes nao exportadas). Fora das abas, abre normalmente.
    /// </summary>
    public async Task ReloadAsync(string path)
    {
        if (FindTab(path) is not { } tab)
        {
            await OpenAsync(path);
            return;
        }
        bool dirty = tab == _activeTab ? IsDirty : tab.IsDirty;
        if (dirty && !await ConfirmAsync("Recarregar save?",
                $"{tab.FileName} mudou no disco, mas a aba tem alterações não exportadas. Recarregar vai descartá-las.",
                "Recarregar", "Manter a aba como está", isDanger: true))
            return;
        if (tab == _activeTab && !await ConfirmDiscardEditAsync())
            return;
        if (CoreAdapter.LoadSave(path) is not { } sav)
        {
            Status = "Arquivo não reconhecido como save.";
            return;
        }
        if (tab != _activeTab)
            StoreActiveTab();
        tab.Replace(sav);
        ActivateTab(tab);
        Status = $"{tab.FileName} recarregado do disco.";
    }

    public void Open(string path)
    {
        // Zip escolhido direto (Abrir save, arrastar, linha de comando): abre o save que estiver dentro.
        string? zipNote = null;
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(path))
        {
            var inside = ZipSaves.ReadAll(path).Select(x => x.Path).ToList();
            if (inside.Count == 0)
            {
                Status = "Nenhum save reconhecido dentro deste .zip.";
                return;
            }
            if (inside.Count > 1)
                zipNote = $" O zip tem {inside.Count} saves; os outros aparecem no Save Manager se o zip estiver na pasta de saves.";
            path = inside[0];
        }
        if (FindTab(path) is { } existing)
        {
            StoreActiveTab();
            ActivateTab(existing);
            Status = $"{existing.FileName} já estava aberto (aba ativada).";
            return;
        }
        var sav = CoreAdapter.LoadSave(path);
        if (sav is null)
        {
            Status = "Arquivo não reconhecido como save.";
            return;
        }
        StoreActiveTab();
        SaveTabViewModel? tab = null;
        tab = new SaveTabViewModel(sav, path)
        {
            SelectCommand = new RelayCommand(() => _ = SwitchToAsync(tab!)),
            CloseCommand = new RelayCommand(() => _ = CloseTabAsync(tab!)),
        };
        OpenSaves.Add(tab);
        Raise(nameof(HasTabs));
        ActivateTab(tab);
        Status = $"Aberto: {ZipSaves.DisplayName(path)}" + zipNote;
        Settings.LastSavePath = ZipSaves.IsZipPath(path, out var zip, out var entry) ? ZipSaves.Combine(System.IO.Path.GetFullPath(zip), entry) : System.IO.Path.GetFullPath(path);
        Settings.RecentSaves.RemoveAll(p => string.Equals(p, Settings.LastSavePath, StringComparison.OrdinalIgnoreCase));
        Settings.RecentSaves.Insert(0, Settings.LastSavePath);
        if (Settings.RecentSaves.Count > 12)
            Settings.RecentSaves.RemoveRange(12, Settings.RecentSaves.Count - 12);
        Settings.Save();
        Raise(nameof(HasLastSave));
        Raise(nameof(ShowLastSaveLink));
        Raise(nameof(LastSaveName));
    }

    /// <summary>Somente depois que o documento externo foi gravado e verificado.</summary>
    public void MarkExternallySaved() => IsDirty = false;

    public string? SaveError { get; private set; }
    public bool Export(string path, bool markSaved = true)
    {
        SaveError = null;
        if (_sav is null)
            return false;
        try
        {
            if (Editor is { IsModified: true } editor)
            {
                if (!editor.CanApply)
                {
                    SaveError = "A edição do Pokémon ainda não pode ser aplicada no modo legal. Corrija a legalidade ou descarte a edição antes de salvar.";
                    Status = SaveError;
                    return false;
                }
                editor.ApplyCommand.Execute(null);
            }
            ApplyPendingPages(); // mochila mudada na tela e ainda nao aplicada
            var reason = path.Equals(CurrentSavePath, StringComparison.OrdinalIgnoreCase) ? "Salvar" : "Salvar como";
            var backup = SaveBackup.BeforeOverwrite(ZipSaves.FileOf(path), reason, CoreAdapter.GetVersionName(_sav.Version));
            CoreAdapter.ExportSave(_sav, path);
            if (markSaved) IsDirty = false;
            var where = ZipSaves.IsZipPath(path, out _, out _) ? ZipSaves.DisplayName(path) : path;
            Status = backup is null
                ? $"Salvo em {where}"
                : $"Salvo em {where}. Backup do arquivo anterior: {System.IO.Path.GetFileName(backup)} (Saves › Backups).";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Erro ao salvar: {ex.Message}";
            SaveError = Status;
            CrashLog.Write(ex);
            return false;
        }
    }

    /// <summary>Grava no save o que as paginas guardam so na tela (hoje, a mochila).</summary>
    private void ApplyPendingPages()
    {
        foreach (var bag in AllPages.OfType<BagPageViewModel>())
            bag.ApplyPending();
    }

    /// <summary>Arquivo de onde o save ativo foi aberto (ou "zip|entrada"), se ainda existir.</summary>
    public string? QuickSavePath => _sav?.Metadata.FilePath is { } p && System.IO.File.Exists(ZipSaves.FileOf(p)) ? p : null;
    public bool CanQuickSave => QuickSavePath is not null;

    /// <summary>
    /// Salvar silencioso (botao Salvar / Ctrl+S): grava por cima do arquivo de onde o save foi aberto, sem janela.
    /// O Export ja faz backup do arquivo anterior. Retorna false se nao ha origem ou se a gravacao falhar.
    /// </summary>
    public bool QuickSave()
    {
        if (QuickSavePath is not { } path)
            return false;
        return Export(path);
    }

    public string? SuggestedFileName => ZipSaves.EntryFileName(_sav?.Metadata.FilePath) ?? _sav?.Metadata.FileName;
    /// <summary>Caminho "zip|entrada" do save aberto, se ele veio de um .zip (Salvar oferece gravar de volta no zip).</summary>
    public string? ZipSavePath => _sav?.Metadata.FilePath is { } p && ZipSaves.IsZipPath(p, out _, out _) ? p : null;

    /// <summary>Arrastar e soltar: move/troca (ou copia, com Ctrl) entre slots de caixa e equipe.</summary>
    public Task MoveSlotAsync(SlotViewModel src, SlotViewModel dst, bool copy)
        => MoveSlotAsync(src, dst, copy ? DropMode.Copy : DropMode.Move);

    /// <summary>Soltar um slot em outro: mover/trocar, copiar (Ctrl ou Shift) ou sobrescrever (Alt).</summary>
    public async Task MoveSlotAsync(SlotViewModel src, SlotViewModel dst, DropMode mode)
    {
        if (_sav is null || src == dst)
            return;
        if (src.IsMarked && _marks.Count > 1)
        {
            await MoveMarkedAsync(dst, mode);
            return;
        }
        if (src.IsOther || dst.IsOther)
        {
            await MoveWithOtherAsync(src, dst, mode);
            return;
        }
        if (src.IsBank || dst.IsBank)
        {
            await MoveWithBankAsync(src, dst, mode);
            return;
        }
        if (mode != DropMode.Move && !dst.IsEmpty && !src.IsEmpty && !await ConfirmAsync("Substituir Pokémon?",
                mode == DropMode.Copy
                    ? $"{dst.Title} ({dst.Location}) será substituído por uma cópia de {src.Title}. Dá para desfazer com Ctrl+Z."
                    : $"{dst.Title} ({dst.Location}) será substituído por {src.Title}, e o slot de origem fica vazio. Dá para desfazer com Ctrl+Z.",
                "Substituir"))
            return;
        MoveSlot(src, dst, mode);
    }

    /// <summary>
    /// Mover/copiar/sobrescrever envolvendo o bank. Bank ↔ bank: arquivos. Save → bank: o Pokemon vai no formato do save.
    /// Bank → save: e convertido para a geracao do save (se nao der, avisa e nada muda). O lado do save entra no
    /// desfazer e precisa ser salvo; o lado do bank e gravado na hora.
    /// </summary>
    private async Task MoveWithBankAsync(SlotViewModel src, SlotViewModel dst, DropMode mode)
    {
        if (_sav is null || src.Pkm is not { } a || src.IsEmpty)
            return;
        var b = dst.IsEmpty ? null : dst.Pkm;
        var name = src.Title;
        // Anexar: do bank para o save vai uma copia; o original fica no bank, ligado ao save.
        bool attach = Bank.AttachMode && src.IsBank && !dst.IsBank && src.BankBox is { IsExternal: false };
        if (attach)
            mode = DropMode.Copy;
        if (mode != DropMode.Move && b is not null && !await ConfirmAsync("Substituir Pokémon?",
                $"{dst.Title} ({dst.Location}) será substituído por {(mode == DropMode.Copy ? "uma cópia de " : "")}{name}.", "Substituir"))
            return;

        var legalNote = "";
        try
        {
            if (src.IsBank && dst.IsBank)
            {
                BankStorage.WriteSlot(dst.BankBox!, dst.Slot, a);
                if (mode == DropMode.Move && b is not null)
                    BankStorage.WriteSlot(src.BankBox!, src.Slot, b);
                else if (mode != DropMode.Copy)
                    BankStorage.DeleteSlot(src.BankBox!, src.Slot);
                Status = $"{name} {(mode == DropMode.Copy ? "copiado" : "movido")} para {dst.Location}.";
            }
            else if (dst.IsBank)
            {
                // save → bank. Se for troca, o Pokemon do bank precisa caber no save.
                PKM? back = null;
                if (mode == DropMode.Move && b is not null && (back = CoreAdapter.ConvertForSave(_sav, b, out var err)) is null)
                {
                    Status = $"Não dá para trocar: {dst.Title} não pode ir para o save ({err}). Solte num slot vazio do bank.";
                    return;
                }
                if (back is not null && !await ConfirmTransferAsync([(b!, back)])) return;
                var srcInfo = CoreAdapter.GetSlotInfo(_sav, src.Box, src.Slot);
                if (mode != DropMode.Copy && back is null && src.IsParty && _sav.IsPartyAllEggs(src.Slot))
                {
                    Status = "A equipe precisa ter pelo menos um Pokémon (que não seja ovo).";
                    return;
                }
                if (mode != DropMode.Copy)
                {
                    _history!.Record($"guardar {name} no bank", SlotHistory.KeyOf(src.Box, src.Slot));
                    OnHistoryChanged();
                }
                BankStorage.WriteSlot(dst.BankBox!, dst.Slot, a.Clone());
                if (back is not null)
                    CoreAdapter.ImportToSlot(_sav, srcInfo, back);
                else if (mode != DropMode.Copy)
                    CoreAdapter.DeleteSlot(_sav, srcInfo);
                if (mode != DropMode.Copy)
                    IsDirty = true;
                Status = mode == DropMode.Copy
                    ? $"{name} copiado para o bank ({dst.Location})."
                    : $"{name} guardado no bank ({dst.Location}). Salve o save para tirá-lo do jogo.";
            }
            else
            {
                // bank → save: converte para a geracao do save.
                if (CoreAdapter.ConvertForSave(_sav, a, out var err) is not { } converted)
                {
                    Status = $"{name} não pode ir para {CoreAdapter.GetGameName(_sav)}: {err}";
                    return;
                }
                var beforeLegalize = converted;
                (converted, legalNote) = await OfferLegalizeAsync(converted, "do bank");
                // Quem aceitou o Legalizar ja viu a previa das mudancas: nao pergunta de novo.
                if (ReferenceEquals(converted, beforeLegalize) && !await ConfirmTransferAsync([(a, converted)])) return;
                var dstInfo = CoreAdapter.GetSlotInfo(_sav, dst.Box, dst.Slot);
                _history!.Record($"trazer {name} do bank", SlotHistory.KeyOf(dst.Box, dst.Slot));
                if (CoreAdapter.ImportToSlot(_sav, dstInfo, converted) is { } error)
                {
                    _history.Discard();
                    Status = error;
                    return;
                }
                OnHistoryChanged();
                if (mode == DropMode.Move && b is not null)
                    BankStorage.WriteSlot(src.BankBox!, src.Slot, b.Clone());
                else if (mode != DropMode.Copy)
                    BankStorage.DeleteSlot(src.BankBox!, src.Slot);
                if (mode != DropMode.Copy)
                    BankLinks.Detach(a); // saiu do bank: o vinculo antigo nao vale mais
                if (attach)
                    BankLinks.Attach(a, CurrentSavePath, SaveLabel);
                IsDirty = true;
                var legal = legalNote != "" ? " " + legalNote : CoreAdapter.IsLegal(converted) == false ? " Atenção: ficou ilegal depois da conversão; veja o cartão de legalidade." : "";
                Status = attach
                    ? $"{name} anexado: a cópia foi para {dst.Location} e o original continua no bank (🔗). Depois de jogar, use “Atualizar anexados”.{legal} Lembre-se de salvar o save."
                    : $"{name} {(mode == DropMode.Copy ? "copiado" : "trazido")} do bank para {dst.Location}.{legal} Lembre-se de salvar o save.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Erro no bank: {ex.Message}";
        }
        Bank.LoadBox();
        RefreshSlots();
    }

    /// <summary>
    /// Mover/copiar/sobrescrever envolvendo o outro save (painel esquerdo da pagina Bank). Entre os dois saves o
    /// Pokemon e convertido para a geracao do destino (numa troca, o que volta tambem). O lado do save principal entra
    /// no Ctrl+Z; o outro save so muda na memoria ate clicar em "Salvar este save".
    /// </summary>
    private async Task MoveWithOtherAsync(SlotViewModel src, SlotViewModel dst, DropMode mode)
    {
        if (_sav is null || src.Pkm is not { } a || src.IsEmpty)
            return;
        if (src.IsBank || dst.IsBank)
        {
            Status = "Para mover entre o bank e o outro save, passe pelo save aberto (ou abra o outro save à direita).";
            return;
        }
        var srcSav = src.OtherSave ?? _sav;
        var dstSav = dst.OtherSave ?? _sav;
        var b = dst.IsEmpty ? null : dst.Pkm;
        var name = src.Title;
        if (mode != DropMode.Move && b is not null && !await ConfirmAsync("Substituir Pokémon?",
                $"{dst.Title} ({dst.Location}) será substituído por {(mode == DropMode.Copy ? "uma cópia de " : "")}{name}.", "Substituir"))
            return;

        var srcInfo = CoreAdapter.GetSlotInfo(srcSav, src.Box, src.Slot);
        var dstInfo = CoreAdapter.GetSlotInfo(dstSav, dst.Box, dst.Slot);
        if (srcSav == dstSav)
        {
            // Dentro do outro save: as regras do Core (slots bloqueados etc.). Entra no desfazer do outro save.
            OtherSave.History?.Record($"{(mode == DropMode.Copy ? "copiar" : "mover")} {name}", SlotHistory.KeyOf(src.Box, src.Slot), SlotHistory.KeyOf(dst.Box, dst.Slot));
            if (CoreAdapter.MoveSlot(srcSav, srcInfo, dstInfo, mode == DropMode.Copy, mode == DropMode.Overwrite) is { } error)
            {
                OtherSave.History?.Discard();
                if (error != "")
                    Status = error;
                return;
            }
            OtherSave.OnHistoryChanged();
            OtherSave.MarkDirty();
            OtherSave.LoadBox();
            Status = $"{name} {(mode == DropMode.Copy ? "copiado" : "movido")} para {dst.Location}. Salve o outro save para gravar.";
            return;
        }

        // Entre os dois saves: converte para a geracao do destino.
        if (CoreAdapter.ConvertForSave(dstSav, a, out var err) is not { } converted)
        {
            Status = $"{name} não pode ir para {CoreAdapter.GetGameName(dstSav)}: {err}";
            return;
        }
        PKM? back = null;
        if (mode == DropMode.Move && b is not null && (back = CoreAdapter.ConvertForSave(srcSav, b, out var err2)) is null)
        {
            Status = $"Não dá para trocar: {dst.Title} não pode ir para {CoreAdapter.GetGameName(srcSav)} ({err2}). Solte num slot vazio.";
            return;
        }
        var legalNote = "";
        var beforeLegalize = converted;
        if (dstSav == _sav)
            (converted, legalNote) = await OfferLegalizeAsync(converted, "do outro save");
        // Quem aceitou o Legalizar ja viu a previa dele; a troca de volta (back) ainda entra na conferencia.
        IReadOnlyList<(PKM, PKM)> review = ReferenceEquals(converted, beforeLegalize) ? [(a, converted)] : [];
        if (back is not null)
            review = [.. review, (b!, back)];
        if (!await ConfirmTransferAsync(review)) return;
        if (mode != DropMode.Copy && back is null && src.IsParty && srcSav.IsPartyAllEggs(src.Slot))
        {
            Status = "A equipe precisa ter pelo menos um Pokémon (que não seja ovo).";
            return;
        }
        if (!dstInfo.CanWriteTo(dstSav) || (mode != DropMode.Copy && !srcInfo.CanWriteTo(srcSav)))
        {
            Status = "Slot bloqueado pelo jogo.";
            return;
        }

        bool mainChanges = dstSav == _sav || mode != DropMode.Copy;
        var mainSlot = dstSav == _sav ? dst : src;
        if (mainChanges)
            _history!.Record($"{(dstSav == _sav ? "trazer" : "enviar")} {name} {(dstSav == _sav ? "do" : "para o")} outro save", SlotHistory.KeyOf(mainSlot.Box, mainSlot.Slot));
        // O lado do outro save entra no desfazer dele.
        var otherSlot = dstSav == _sav ? src : dst;
        bool otherChanges = dstSav != _sav || mode != DropMode.Copy;
        if (otherChanges)
            OtherSave.History?.Record($"{(dstSav == _sav ? "enviar" : "receber")} {name}", SlotHistory.KeyOf(otherSlot.Box, otherSlot.Slot));
        try
        {
            CoreAdapter.ImportToSlot(dstSav, dstInfo, converted);
            if (back is not null)
                CoreAdapter.ImportToSlot(srcSav, srcInfo, back);
            else if (mode != DropMode.Copy)
                CoreAdapter.DeleteSlot(srcSav, srcInfo);
        }
        catch (Exception ex)
        {
            Status = $"Erro ao mover: {ex.Message}";
        }
        if (mainChanges)
        {
            OnHistoryChanged();
            IsDirty = true;
        }
        if (otherChanges)
        {
            OtherSave.MarkDirty();
            OtherSave.OnHistoryChanged();
        }
        OtherSave.LoadBox();
        RefreshSlots();
        var legal = legalNote != "" ? " " + legalNote : CoreAdapter.IsLegal(converted) == false ? " Atenção: ficou ilegal depois da conversão." : "";
        Status = $"{name} {(mode == DropMode.Copy ? "copiado" : "movido")} para {dst.Location}.{legal} Salve os dois saves para gravar (Ctrl+Z desfaz o save aberto; o outro save tem ↶ próprio no painel).";
    }

    public void MoveSlot(SlotViewModel src, SlotViewModel dst, bool copy) => MoveSlot(src, dst, copy ? DropMode.Copy : DropMode.Move);

    public void MoveSlot(SlotViewModel src, SlotViewModel dst, DropMode mode)
    {
        if (_sav is null)
            return;
        var copy = mode == DropMode.Copy;
        var srcName = src.Title;
        _history!.Record(mode switch { DropMode.Copy => $"copiar {srcName}", DropMode.Overwrite => $"sobrescrever com {srcName}", _ => $"mover {srcName}" },
            SlotHistory.KeyOf(src.Box, src.Slot), SlotHistory.KeyOf(dst.Box, dst.Slot));
        var error = CoreAdapter.MoveSlot(_sav,
            CoreAdapter.GetSlotInfo(_sav, src.Box, src.Slot),
            CoreAdapter.GetSlotInfo(_sav, dst.Box, dst.Slot), copy, mode == DropMode.Overwrite);
        if (error is not null)
        {
            _history.Discard();
            if (error != "") // "" = nada a fazer
                Status = error;
            return;
        }
        OnHistoryChanged();
        IsDirty = true;
        RefreshSlots();
        Status = mode switch
        {
            DropMode.Copy => $"{srcName} copiado para {dst.Location}. Lembre-se de exportar o save.",
            DropMode.Overwrite => $"{srcName} sobrescreveu {dst.Location} (a origem ficou vazia). Lembre-se de exportar o save.",
            _ => $"{srcName} movido para {dst.Location}. Lembre-se de exportar o save.",
        };
    }

    /// <summary>
    /// Pokemon vindo de fora (arquivo, bank, outro save) que chega ilegal ao save aberto: com o modo legal ligado,
    /// oferece o Legalizar antes de gravar. Retorna o Pokemon a gravar (legalizado ou o original) e uma nota para o status.
    /// </summary>
    private async Task<(PKM Pk, string Note)> OfferLegalizeAsync(PKM pk, string origin)
    {
        if (_sav is null || CoreAdapter.IsLegal(pk) != false)
            return (pk, "");
        var name = CoreAdapter.SpeciesNames[pk.Species];
        if (!LegalMode)
            return (pk, $"Atenção: {name} está ilegal (dá para usar ✨ Legalizar no editor).");
        var candidate = await LegalizeOutsideAsync(pk);
        if (ReferenceEquals(candidate.Pk, pk))
            return candidate; // nao deu para legalizar: entra como esta, com o motivo na nota
        var legalize = await ConfirmAsync($"{name} está ilegal",
            $"{name} ({origin}) não é legal neste save. Legalizar gera de novo a partir de um encontro real de {CoreAdapter.GetGameName(_sav)}, mantendo natureza, nível, item, apelido e golpes quando possível.",
            "✨ Legalizar", "Trazer como está", details: [candidate.Note, .. PokemonDiff.Details(pk, candidate.Pk)], icon: "🛡");
        if (!legalize)
            return (pk, $"Atenção: {name} entrou ilegal.");
        return candidate;
    }

    private async Task<(PKM Pk, string Note)> LegalizeOutsideAsync(PKM pk)
    {
        var name = CoreAdapter.SpeciesNames[pk.Species];
        var sav = _sav!;
        Status = $"Legalizando {name}...";
        try
        {
            var current = pk.Clone();
            var (result, message) = await Task.Run(() => (EncounterDatabase.Legalize(sav, current, out var m), m));
            return result is null
                ? (pk, $"Não deu para legalizar {name} ({message}); entrou como estava.")
                : (result, $"{name} legalizado a partir de: {message}.");
        }
        catch (Exception ex)
        {
            return (pk, $"Erro ao legalizar {name} ({ex.Message}); entrou como estava.");
        }
    }

    /// <summary>
    /// Conferencia antes de gravar uma transferencia (conversao de geracao e/ou legalizacao). So pergunta quando
    /// muda algo que o usuario costuma querer preservar (⚠: shiny, PID, IVs, natureza, OT, apelido, Pokebola, golpes);
    /// conversoes que so mexem em campos comuns passam direto, sem uma janela a cada arraste.
    /// </summary>
    private Task<bool> ConfirmTransferAsync(IReadOnlyList<(PKM Before, PKM After)> pairs)
    {
        var changed = pairs.Where(p => PokemonDiff.HasImportantChanges(p.Before, p.After)).ToArray();
        if (changed.Length == 0) return Task.FromResult(true);
        var message = Loc.T("Confira as mudanças antes de transferir. O save só será gravado ao salvar.");
        if (changed.Length > 1) message += "\n" + PokemonDiff.BatchSummary(changed);
        return ConfirmAsync("Prévia da transferência", message,
            "Transferir", details: changed.Length == 1 ? PokemonDiff.Details(changed[0].Before, changed[0].After) : PokemonDiff.Batch(changed), icon: "↔", detailsExpanded: changed.Length == 1);
    }

    /// <summary>Soltar um arquivo .pk* sobre um slot (pergunta antes de substituir um Pokemon).</summary>
    public async Task ImportFileAsync(SlotViewModel dst, string path)
    {
        if (_sav is null)
            return;
        if (!await ConfirmDiscardEditAsync()) return;
        if (dst.IsOther)
        {
            Status = "Importe o arquivo numa caixa ou na equipe do save ativo.";
            return;
        }
        var importedFile = PKHeX.Core.FileUtil.GetSupportedFile(path, _sav);
        if (importedFile is MysteryGift { IsEntity: false })
        {
            Status = "Este Mystery Gift contém itens e não pode gerar um Pokémon no slot.";
            return;
        }
        if (dst is { IsBank: true, BankBox: { } bankBox })
        {
            // No bank o arquivo entra como esta (sem conversao).
            var raw = importedFile as PKM;
            if (importedFile is MysteryGift { IsEntity: true } bankGift)
                raw = EncounterDatabase.ToEntity(_sav, bankGift, out _);
            if (raw is null)
            {
                Status = "Arquivo não reconhecido como Pokémon.";
                return;
            }
            if (!dst.IsEmpty && !await ConfirmAsync("Substituir Pokémon?", $"{dst.Title} ({dst.Location}) será substituído pelo arquivo.", "Substituir"))
                return;
            BankStorage.WriteSlot(bankBox, dst.Slot, raw);
            Bank.LoadBox();
            Status = $"{CoreAdapter.SpeciesNames[raw.Species]} importado para o bank ({dst.Location}).";
            return;
        }
        var pk = CoreAdapter.LoadEntityFile(_sav, path);
        if (pk is null)
        {
            Status = "Arquivo de Pokémon ou Mystery Gift incompatível com este save.";
            return;
        }
        if (!dst.IsEmpty && !await ConfirmAsync("Substituir Pokémon?",
                $"{dst.Title} ({dst.Location}) será substituído por {CoreAdapter.SpeciesNames[pk.Species]} do arquivo. Dá para desfazer com Ctrl+Z.",
                "Substituir"))
            return;
        (pk, var note) = await OfferLegalizeAsync(pk, "arquivo " + System.IO.Path.GetFileName(path));
        ImportEntity(dst, pk);
        if (note != "")
            Status += " " + note;
    }

    private void ImportEntity(SlotViewModel dst, PKM pk)
    {
        if (_sav is null)
            return;
        _history!.Record($"importar {CoreAdapter.SpeciesNames[pk.Species]}", SlotHistory.KeyOf(dst.Box, dst.Slot));
        var error = CoreAdapter.ImportToSlot(_sav, CoreAdapter.GetSlotInfo(_sav, dst.Box, dst.Slot), pk);
        if (error is not null)
        {
            _history.Discard();
            Status = error;
            return;
        }
        OnHistoryChanged();
        IsDirty = true;
        RefreshSlots();
        Status = $"{CoreAdapter.SpeciesNames[pk.Species]} importado em {dst.Location}.";
    }

    /// <summary>Grava o Pokemon do slot selecionado num arquivo .pk*.</summary>
    public void ExportEntity(string path)
    {
        if (_selectedSlot is not { IsEmpty: false } slot)
            return;
        try
        {
            CoreAdapter.ExportEntity(slot.Pkm!, path);
            Status = $"{slot.Title} exportado para {path}";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao exportar: {ex.Message}";
        }
    }

    /// <summary>Excluir (Delete): apaga o Pokemon selecionado. Da para desfazer com Ctrl+Z.</summary>
    public async Task DeleteSelectedAsync()
    {
        if (HasMarks)
        {
            await DeleteMarkedAsync();
            return;
        }
        if (_sav is null || _selectedSlot is not { IsEmpty: false } slot)
            return;
        var name = slot.Title;
        if (slot is { IsOther: true, OtherSave: { } other })
        {
            if (!await ConfirmAsync("Excluir do outro save?", $"{name} ({slot.Location}) será apagado do outro save (só vale depois de salvá-lo; não entra no Ctrl+Z).", "Excluir", isDanger: true))
                return;
            if (CoreAdapter.DeleteSlot(other, CoreAdapter.GetSlotInfo(other, slot.Box, slot.Slot)) is { Length: > 0 } otherError)
            {
                Status = otherError;
                return;
            }
            OtherSave.MarkDirty();
            OtherSave.LoadBox();
            _selectedSlot = null;
            RaiseSelectionChanged();
            Status = $"{name} excluído do outro save. Salve-o para gravar.";
            return;
        }
        if (slot is { IsBank: true, BankBox: { } bankBox })
        {
            if (!await ConfirmAsync("Excluir do bank?", $"{name} ({slot.Location}) será apagado do bank. Isso não pode ser desfeito.", "Excluir", isDanger: true))
                return;
            if (slot.Pkm is { } gone)
                BankLinks.Detach(gone);
            BankStorage.DeleteSlot(bankBox, slot.Slot);
            _selectedSlot = null;
            Bank.LoadBox();
            RaiseSelectionChanged();
            Status = $"{name} excluído do bank.";
            return;
        }
        if (!await ConfirmAsync("Excluir Pokémon?", $"{name} ({slot.Location}) será apagado. Dá para desfazer com Ctrl+Z.",
                "Excluir", isDanger: true))
            return;
        _history!.Record($"excluir {name}", SlotHistory.KeyOf(slot.Box, slot.Slot));
        var error = CoreAdapter.DeleteSlot(_sav, CoreAdapter.GetSlotInfo(_sav, slot.Box, slot.Slot));
        if (error is not null)
        {
            _history.Discard();
            if (error != "")
                Status = error;
            return;
        }
        IsDirty = true;
        OnHistoryChanged();
        RefreshSlots();
        Status = $"{name} excluído de {slot.Location}. Ctrl+Z desfaz.";
    }

    /// <summary>Importar (botao): vai para o slot selecionado ou, sem selecao, para o primeiro slot vazio da caixa.</summary>
    public async Task ImportFileAsync(string path)
    {
        var dst = _selectedSlot ?? Boxes.Slots.FirstOrDefault(s => s.IsEmpty);
        if (dst is null)
        {
            Status = "Não há slot vazio nesta caixa. Selecione um slot para substituir.";
            return;
        }
        await ImportFileAsync(dst, path);
    }

    /// <summary>Verifica a legalidade da caixa atual e da equipe.</summary>
    private bool _checkingLegality;

    /// <summary>
    /// Verificar legalidade: analisa o save inteiro (todas as caixas e a equipe) em segundo plano e mostra o
    /// resultado numa janela, com a lista do que tem problema e um atalho para o primeiro.
    /// </summary>
    public async Task CheckLegalityAsync()
    {
        if (_sav is null || _checkingLegality)
            return;
        _checkingLegality = true;
        CheckLegalityCommand.NotifyCanExecuteChanged();
        Status = "Verificando a legalidade de todo o save...";
        try
        {
            var sav = _sav;
            var all = EntitySearch.ReadAll(sav);
            var bad = await Task.Run(() => all
                .Where(e => CoreAdapter.IsLegal(e.Pkm) == false)
                .Select(e => (Entity: e, Issues: CoreAdapter.GetLegalityIssues(e.Pkm, 1))) // motivo so para os ilegais
                .ToList());

            string Where(StoredEntity e) => e.Box < 0 ? $"Equipe · {e.Slot + 1}" : $"{CoreAdapter.GetBoxName(sav, e.Box)} · {e.Slot + 1}";
            string Name(PKM pk) => pk.IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[pk.Species];

            if (bad.Count == 0)
            {
                Status = all.Count == 1 ? "Legalidade: o Pokémon do save é legal." : $"Legalidade: os {all.Count} Pokémon do save são legais.";
                await ConfirmAsync("Tudo legal", all.Count == 1
                        ? "O único Pokémon do save passou na verificação de legalidade."
                        : $"Os {all.Count} Pokémon do save (todas as caixas e a equipe) passaram na verificação de legalidade.",
                    "OK", cancelText: "", icon: "✓");
                return;
            }

            Status = $"Legalidade: {bad.Count} de {all.Count} Pokémon com problema.";
            const int max = 30;
            var lines = bad.Take(max)
                .Select(r => $"⚠ {Name(r.Entity.Pkm)} ({Where(r.Entity)}): {r.Issues.FirstOrDefault() ?? "ilegal"}")
                .ToList();
            if (bad.Count > max)
                lines.Add($"… e mais {bad.Count - max}.");
            var goTo = await ConfirmAsync($"{bad.Count} Pokémon com problema",
                $"De {all.Count} Pokémon no save, {bad.Count} não passaram na verificação. Abra cada um no editor e use as correções do cartão de legalidade (ou Legalizar).",
                "Ir para o primeiro", "Fechar", isDanger: true, details: lines, icon: "⚠");
            if (goTo)
                await GoToSearchHitAsync(new SearchHitViewModel(bad[0].Entity, "", ""));
        }
        catch (Exception ex)
        {
            Status = $"Erro ao verificar a legalidade: {ex.Message}";
        }
        finally
        {
            _checkingLegality = false;
            CheckLegalityCommand.NotifyCanExecuteChanged();
        }
    }

    // Selecao multipla: Ctrl+clique marca/desmarca, Shift+clique marca um intervalo, Ctrl+A marca a caixa toda.
    // Vale para caixas do save e do bank (inclusive caixas diferentes); a equipe fica de fora, porque as
    // posicoes dela mudam quando alguem sai. As marcas sao posicoes: qualquer alteracao nos slots as limpa.
    /// <summary>Um slot marcado: caixa do save aberto, do outro save (<see cref="Other"/>) ou do bank.</summary>
    public readonly record struct MarkKey(int Box, BankBox? Bank, int Slot, bool Other = false);
    private readonly List<MarkKey> _marks = [];
    private MarkKey? _markAnchor;

    private static MarkKey KeyOf(SlotViewModel s) => new(s.Box, s.BankBox, s.Slot, s.IsOther);
    private static bool SameBox(MarkKey a, MarkKey b) => a.Box == b.Box && a.Bank == b.Bank && a.Other == b.Other;
    /// <summary>Save do slot marcado (null = bank).</summary>
    private SaveFile? SaveOf(MarkKey k) => k.Bank is not null ? null : k.Other ? OtherSave.Sav : _sav;

    public bool HasMarks => _marks.Count > 0;
    public string MarkedText => _marks.Count == 1 ? "1 selecionado" : $"{_marks.Count} selecionados";
    public RelayCommand ClearMarksCommand { get; }
    public RelayCommand DeleteMarkedCommand { get; }

    /// <summary>Ctrl+clique (alterna) ou Shift+clique (marca do ultimo marcado ate este, na mesma caixa).</summary>
    public void ToggleMark(SlotViewModel slot, bool range)
    {
        if (_sav is null || slot.IsParty || slot.IsEmpty)
            return;
        var key = KeyOf(slot);
        if (range && _markAnchor is { } anchor && SameBox(anchor, key))
        {
            var slots = slot.IsBank ? Bank.Slots : slot.IsOther ? OtherSave.Slots : Boxes.Slots;
            int from = Math.Min(anchor.Slot, key.Slot), to = Math.Max(anchor.Slot, key.Slot);
            foreach (var s in slots.Where(s => s.Slot >= from && s.Slot <= to && !s.IsEmpty))
                if (!_marks.Contains(KeyOf(s)))
                    _marks.Add(KeyOf(s));
        }
        else if (!_marks.Remove(key))
            _marks.Add(key);
        _markAnchor = key;
        // A selecao simples sai de cena para nao confundir (o editor fica para um Pokemon so).
        if (_selectedSlot is not null && Editor is not { IsModified: true })
        {
            _selectedSlot.IsSelected = false;
            _selectedSlot = null;
            Editor = null;
        }
        OnMarksChanged();
    }

    /// <summary>Ctrl+A: marca todos os Pokemon da caixa onde esta a selecao (ou da caixa aberta do save).</summary>
    public void MarkAll()
    {
        if (_sav is null)
            return;
        var slots = _markAnchor is { Other: true } ? OtherSave.Slots
            : _markAnchor is { Bank: not null } || CurrentPage == Bank && _selectedSlot is { IsBank: true } ? Bank.Slots : Boxes.Slots;
        foreach (var s in slots.Where(s => !s.IsEmpty))
            if (!_marks.Contains(KeyOf(s)))
                _marks.Add(KeyOf(s));
        if (slots.FirstOrDefault(s => !s.IsEmpty) is { } first)
            _markAnchor = KeyOf(first);
        OnMarksChanged();
    }

    public void ClearMarks()
    {
        if (_marks.Count == 0 && _markAnchor is null)
            return;
        _marks.Clear();
        _markAnchor = null;
        OnMarksChanged();
    }

    private void OnMarksChanged()
    {
        ApplyMarks();
        Raise(nameof(HasMarks));
        Raise(nameof(HasDesktopMarks));
        Raise(nameof(MarkedText));
        Raise(nameof(MarkedCount));
        RaiseSelectionChanged();
    }

    /// <summary>Reaplica o destaque das marcas nos slots visiveis (depois de trocar de caixa).</summary>
    private void ApplyMarks()
    {
        foreach (var s in Boxes.Slots.Concat(Bank.Slots).Concat(OtherSave.Slots))
            s.IsMarked = _marks.Contains(KeyOf(s));
    }

    /// <summary>Le os Pokemon marcados (na ordem: caixas do save, depois bank; dentro da caixa, por slot).</summary>
    private List<(MarkKey Key, PKM Pkm, string Name)> ResolveMarks()
    {
        var list = new List<(MarkKey, PKM, string)>();
        if (_sav is null)
            return list;
        foreach (var k in _marks.OrderBy(k => k.Bank is not null).ThenBy(k => k.Other).ThenBy(k => k.Bank?.Folder).ThenBy(k => k.Box).ThenBy(k => k.Slot))
        {
            var pk = k.Bank is { } bank ? BankStorage.ReadSlot(bank, k.Slot)
                : SaveOf(k) is { } sav ? CoreAdapter.GetBoxSlot(sav, k.Box, k.Slot) : null;
            if (pk is not null && !CoreAdapter.IsEmpty(pk))
                list.Add((k, pk, pk.IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[pk.Species]));
        }
        return list;
    }

    private string MarkLocation(MarkKey k) => k.Bank is { } b ? $"bank › {b.Name} · {k.Slot + 1}"
        : k.Other && OtherSave.Sav is { } o ? $"{OtherSave.FileName} › {CoreAdapter.GetBoxName(o, k.Box)} · {k.Slot + 1}"
        : $"{CoreAdapter.GetBoxName(_sav!, k.Box)} · {k.Slot + 1}";

    /// <summary>Excluir os marcados. Os do save entram no desfazer (um passo so); os do bank sao apagados na hora.</summary>
    private async Task DeleteMarkedAsync()
    {
        if (_sav is null)
            return;
        var items = ResolveMarks();
        if (items.Count == 0)
        {
            ClearMarks();
            return;
        }
        int inBank = items.Count(i => i.Key.Bank is not null);
        var message = inBank == 0
            ? $"{items.Count} Pokémon serão apagados do save. Dá para desfazer com Ctrl+Z."
            : inBank == items.Count
                ? $"{items.Count} Pokémon serão apagados do bank. Isso não pode ser desfeito."
                : $"{items.Count} Pokémon serão apagados ({inBank} do bank, que não volta com Ctrl+Z).";
        var details = items.Take(30).Select(i => $"{i.Name} ({MarkLocation(i.Key)})").ToList();
        if (items.Count > 30)
            details.Add($"… e mais {items.Count - 30}.");
        if (!await ConfirmAsync($"Excluir {items.Count} Pokémon?", message, "Excluir", isDanger: true, details: details, icon: "🗑"))
            return;

        var fromSave = items.Where(i => i.Key.Bank is null && !i.Key.Other).ToList();
        var fromOther = items.Where(i => i.Key.Other).ToList();
        int deleted = 0;
        if (fromOther.Count > 0 && OtherSave.Sav is { } otherSav)
        {
            OtherSave.History?.Record($"excluir {fromOther.Count} Pokémon", [.. fromOther.Select(i => SlotHistory.KeyOf(i.Key.Box, i.Key.Slot))]);
            foreach (var i in fromOther)
                if (CoreAdapter.DeleteSlot(otherSav, CoreAdapter.GetSlotInfo(otherSav, i.Key.Box, i.Key.Slot)) is null)
                    deleted++;
            OtherSave.MarkDirty();
            OtherSave.OnHistoryChanged();
            OtherSave.LoadBox();
        }
        if (fromSave.Count > 0)
        {
            _history!.Record($"excluir {fromSave.Count} Pokémon", [.. fromSave.Select(i => SlotHistory.KeyOf(i.Key.Box, i.Key.Slot))]);
            foreach (var i in fromSave)
                if (CoreAdapter.DeleteSlot(_sav, CoreAdapter.GetSlotInfo(_sav, i.Key.Box, i.Key.Slot)) is null)
                    deleted++;
            if (deleted == 0)
                _history.Discard();
            else
                IsDirty = true;
            OnHistoryChanged();
        }
        foreach (var i in items.Where(i => i.Key.Bank is not null).OrderByDescending(i => i.Key.Bank!.ExternalIndex).ThenByDescending(i => i.Key.Slot))
        {
            BankLinks.Detach(i.Pkm);
            BankStorage.DeleteSlot(i.Key.Bank!, i.Key.Slot);
            deleted++;
        }
        Bank.LoadBox();
        RefreshSlots();
        Status = deleted == items.Count
            ? $"{deleted} Pokémon excluídos.{(fromSave.Count > 0 ? " Ctrl+Z desfaz (os do save)." : "")}"
            : $"{deleted} de {items.Count} Pokémon excluídos (os outros estão em slots bloqueados pelo jogo).";
    }

    /// <summary>
    /// Arrastar um grupo marcado: os Pokemon vao para os slots livres da caixa de destino (save ou bank), a partir
    /// do slot onde foram soltos. Ctrl/Shift copia. Do bank para o save, cada um e convertido; quem nao puder
    /// entrar no jogo fica onde esta.
    /// </summary>
    private async Task MoveMarkedAsync(SlotViewModel dst, DropMode mode)
    {
        if (_sav is null)
            return;
        if (dst.IsParty)
        {
            Status = "Vários Pokémon de uma vez só vão para caixas (do save, do outro save ou do bank). Para a equipe, arraste um por vez.";
            return;
        }
        bool copy = mode == DropMode.Copy;
        var target = KeyOf(dst);
        bool InTarget(MarkKey k) => SameBox(k, target);
        // Destino: save aberto, outro save ou bank (null).
        var dstSav = dst.IsBank ? null : dst.IsOther ? OtherSave.Sav : _sav;
        if (!dst.IsBank && dstSav is null)
            return;

        // Anexar: todos vindo do bank para um save -> vao copias; os originais ficam no bank, ligados ao save.
        var marked = ResolveMarks();
        bool attach = Bank.AttachMode && dstSav is not null && marked.Count > 0 && marked.All(m => m.Key.Bank is { IsExternal: false });
        if (attach)
            copy = true;
        var originals = marked.ToDictionary(m => m.Key, m => m.Pkm);

        // 1) O que vai: entre jogos diferentes (ou do bank para um save) cada um e convertido; slots bloqueados nao saem.
        var moving = new List<(MarkKey Key, PKM Data, string Name)>();
        var skipped = new List<string>();
        foreach (var (key, pk, name) in marked)
        {
            if (!copy && SaveOf(key) is { } srcSav && !CoreAdapter.CanWriteBoxSlot(srcSav, key.Box, key.Slot))
            {
                skipped.Add($"{name} ({MarkLocation(key)}): slot bloqueado pelo jogo");
                continue;
            }
            var data = pk.Clone();
            if (dstSav is not null && SaveOf(key) != dstSav)
            {
                if (CoreAdapter.ConvertForSave(dstSav, pk, out var err) is not { } converted)
                {
                    skipped.Add($"{name} ({MarkLocation(key)}): {err}");
                    continue;
                }
                data = converted;
            }
            moving.Add((key, data, name));
        }
        if (moving.Count == 0)
        {
            await ShowSkippedAsync("Nenhum Pokémon pôde ser movido", skipped);
            return;
        }

        // De fora para o save aberto (bank ou outro save): oferece o Legalizar para os que chegam ilegais (uma pergunta para o grupo).
        var legalNote = "";
        var illegal = dstSav != _sav ? [] : moving.Select((m, i) => (m, i)).Where(x => SaveOf(x.m.Key) != _sav && CoreAdapter.IsLegal(x.m.Data) == false).ToList();
        // Pergunta antes de legalizar (cada um pode levar segundos); a previa das mudancas vem na conferencia logo abaixo.
        if (illegal.Count > 0 && LegalMode && await ConfirmAsync($"{illegal.Count} Pokémon ilegais",
                $"Estes Pokémon de fora não são legais em {CoreAdapter.GetGameName(_sav)}. Legalizar gera cada um de novo a partir de um encontro real do jogo, mantendo natureza, nível, item, apelido e golpes quando possível. Antes de gravar, você confere o que muda.",
                "✨ Legalizar", "Trazer como estão", details: [.. illegal.Select(x => x.m.Name)], icon: "🛡"))
        {
            int ok = 0;
            foreach (var (m, i) in illegal)
            {
                var (pk, _) = await LegalizeOutsideAsync(m.Data);
                if (CoreAdapter.IsLegal(pk) == true)
                    ok++;
                moving[i] = (m.Key, pk, m.Name);
            }
            legalNote = $" {ok} de {illegal.Count} legalizados.";
        }
        else if (illegal.Count > 0)
            legalNote = $" Atenção: {illegal.Count} entraram ilegais.";

        if (!await ConfirmTransferAsync(moving.Select(m => (originals[m.Key], m.Data)).ToArray())) return;

        // 2) Onde cabe: slots livres da caixa de destino (os que estao saindo dela contam como livres).
        HashSet<int> freed = copy ? [] : moving.Where(m => InTarget(m.Key)).Select(m => m.Key.Slot).ToHashSet();
        int size = dstSav is null ? BankStorage.SlotsPerBox : dstSav.BoxSlotCount;
        bool Free(int i) => freed.Contains(i) || (dstSav is null
            ? BankStorage.IsSlotFree(dst.BankBox!, i)
            : CoreAdapter.CanWriteBoxSlot(dstSav, dst.Box, i) && CoreAdapter.IsEmpty(CoreAdapter.GetBoxSlot(dstSav, dst.Box, i)));
        var slots = Enumerable.Range(0, size).Select(i => (dst.Slot + i) % size).Where(Free).Take(moving.Count).ToList();
        if (slots.Count < moving.Count)
        {
            Status = $"Não cabe: a caixa de destino tem {slots.Count} espaço(s) livre(s) para {moving.Count} Pokémon. Escolha outra caixa ou libere espaço.";
            return;
        }

        // 3) Desfazer: um passo em cada save envolvido (o aberto e o outro tem historicos proprios).
        List<SlotHistory.Key> KeysFor(SaveFile? sav)
        {
            var keys = new List<SlotHistory.Key>();
            if (sav is null)
                return keys;
            if (!copy)
                keys.AddRange(moving.Where(m => SaveOf(m.Key) == sav).Select(m => SlotHistory.KeyOf(m.Key.Box, m.Key.Slot)));
            if (dstSav == sav)
                keys.AddRange(slots.Select(i => SlotHistory.KeyOf(dst.Box, i)));
            return keys;
        }
        var description = $"{(copy ? "copiar" : "mover")} {moving.Count} Pokémon";
        var mainKeys = KeysFor(_sav);
        var otherKeys = KeysFor(OtherSave.Sav);
        if (mainKeys.Count > 0)
            _history!.Record(description, [.. mainKeys]);
        if (otherKeys.Count > 0)
            OtherSave.History?.Record(description, [.. otherKeys]);

        try
        {
            void Clear(MarkKey k)
            {
                if (k.Bank is { } bank)
                    BankStorage.DeleteSlot(bank, k.Slot);
                else if (SaveOf(k) is { } sav)
                    CoreAdapter.DeleteSlot(sav, CoreAdapter.GetSlotInfo(sav, k.Box, k.Slot));
            }
            // Primeiro esvazia as origens que ficam na propria caixa de destino, depois grava, por ultimo tira das outras origens
            // (assim, se algo falhar no meio, nenhum Pokemon some).
            // (em pasta externa os arquivos seguintes sobem ao apagar: apaga do ultimo para o primeiro)
            if (!copy)
                foreach (var m in moving.Where(m => InTarget(m.Key)).OrderByDescending(m => m.Key.Bank?.ExternalIndex ?? 0).ThenByDescending(m => m.Key.Slot))
                    Clear(m.Key);
            for (int i = 0; i < moving.Count; i++)
            {
                if (dstSav is null)
                    BankStorage.WriteSlot(dst.BankBox!, slots[i], moving[i].Data);
                else
                    CoreAdapter.ImportToSlot(dstSav, CoreAdapter.GetSlotInfo(dstSav, dst.Box, slots[i]), moving[i].Data);
            }
            if (!copy)
                foreach (var m in moving.Where(m => !InTarget(m.Key)).OrderByDescending(m => m.Key.Bank?.ExternalIndex ?? 0).ThenByDescending(m => m.Key.Slot))
                    Clear(m.Key);
            // Vinculos: saindo do bank, o vinculo antigo cai; anexando, cada original fica ligado ao save de destino.
            foreach (var m in moving.Where(m => m.Key.Bank is not null))
            {
                if (!copy && dstSav is not null)
                    BankLinks.Detach(originals[m.Key]);
                if (attach)
                    BankLinks.Attach(originals[m.Key], dst.IsOther ? OtherSave.FilePath ?? "" : CurrentSavePath, dst.IsOther ? OtherSave.GameName : SaveLabel);
            }
        }
        catch (Exception ex)
        {
            Status = $"Erro ao mover: {ex.Message}";
        }
        if (mainKeys.Count > 0)
        {
            IsDirty = true;
            OnHistoryChanged();
        }
        if (otherKeys.Count > 0)
        {
            OtherSave.MarkDirty();
            OtherSave.OnHistoryChanged();
        }
        Bank.LoadBox();
        OtherSave.LoadBox();
        RefreshSlots();
        var where = dstSav is null ? $"bank › {dst.BankBox!.Name}"
            : dst.IsOther ? $"{OtherSave.FileName} › {CoreAdapter.GetBoxName(dstSav, dst.Box)}"
            : CoreAdapter.GetBoxName(dstSav, dst.Box);
        Status = $"{moving.Count} Pokémon {(attach ? "anexados (cópias)" : copy ? "copiados" : "movidos")} para {where}."
                 + (attach ? " Os originais continuam no bank (🔗); depois de jogar, use “Atualizar anexados”." : "")
                 + legalNote
                 + (mainKeys.Count > 0 ? " Lembre-se de salvar o save." : "")
                 + (otherKeys.Count > 0 ? " Salve também o outro save (Ctrl+Z dele fica no painel)." : "")
                 + (skipped.Count > 0 ? $" {skipped.Count} ficaram onde estavam." : "");
        if (skipped.Count > 0)
            await ShowSkippedAsync($"{skipped.Count} Pokémon ficaram onde estavam", skipped);
    }

    private Task<bool> ShowSkippedAsync(string title, List<string> skipped)
        => ConfirmAsync(title, "Estes Pokémon não puderam ir para o destino:", "OK", cancelText: "", details: skipped, icon: "⚠");

    /// <summary>Ordenar caixas do save (a caixa aberta ou todas). Um passo no desfazer.</summary>
    private void SortBoxes(CoreAdapter.BoxSortOption option, bool all)
    {
        if (_sav is null)
            return;
        int first = all ? 0 : Boxes.CurrentBox, last = all ? _sav.BoxCount - 1 : Boxes.CurrentBox;
        var keys = new List<SlotHistory.Key>();
        for (int b = first; b <= last; b++)
            for (int i = 0; i < _sav.BoxSlotCount; i++)
                keys.Add(SlotHistory.KeyOf(b, i));
        _history!.Record($"ordenar {(all ? "todas as caixas" : CoreAdapter.GetBoxName(_sav, first))} ({option.Name})", [.. keys]);
        string? error = null;
        try
        {
            CoreAdapter.SortBoxes(_sav, option, first, last);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        IsDirty = true;
        OnHistoryChanged();
        RefreshSlots();
        Status = error is not null ? $"Erro ao ordenar: {error}. Ctrl+Z volta ao estado anterior." : $"{(all ? "Todas as caixas ordenadas" : $"{CoreAdapter.GetBoxName(_sav, first)} ordenada")}: {option.Name}. Ctrl+Z desfaz.";
    }

    /// <summary>Criar PKM: abre o editor em branco no primeiro slot vazio da caixa atual.</summary>
    private async void CreateInFirstEmpty()
    {
        CurrentPage = Boxes;
        if (Boxes.Slots.FirstOrDefault(s => s.IsEmpty) is not { } slot)
        {
            Status = "Esta caixa está cheia. Escolha outra caixa ou um slot vazio.";
            return;
        }
        if (!await ConfirmDiscardEditAsync())
            return;
        SelectSlot(slot);
        Status = $"Novo Pokémon em {slot.Location}. Escolha a espécie (ou cole um set Showdown) e clique em Aplicar.";
    }

    /// <summary>A selecao mudou: atualiza Exportar PKM e Excluir.</summary>
    private void RaiseSelectionChanged()
    {
        Raise(nameof(CanExportEntity));
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void Undo()
    {
        if (CurrentPage == Game)
        {
            if (Game.UndoGame() is { } description) { RefreshSlots(); Status = $"Desfeito: {description}."; }
            return;
        }
        if (_history?.Undo() is not { } what)
            return;
        IsDirty = true;
        OnHistoryChanged();
        RefreshSlots();
        Status = $"Desfeito: {what}.";
    }

    private void Redo()
    {
        if (CurrentPage == Game)
        {
            if (Game.RedoGame() is { } description) { RefreshSlots(); Status = $"Refeito: {description}."; }
            return;
        }
        if (_history?.Redo() is not { } what)
            return;
        IsDirty = true;
        OnHistoryChanged();
        RefreshSlots();
        Status = $"Refeito: {what}.";
    }

    private void OnHistoryChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        Raise(nameof(UndoTip));
        Raise(nameof(RedoTip));
        Raise(nameof(PendingActions));
        Raise(nameof(PendingText));
    }

    // Edicao em lote
    /// <summary>Pokemon do save aberto em cada escopo da edicao em lote (lidos agora, com as alteracoes da aba).</summary>
    private IReadOnlyList<BatchTarget> GetBatchTargets(BatchScope scope)
    {
        if (_sav is not { } sav)
            return [];
        var list = new List<BatchTarget>();
        void AddBox(int b, int i)
        {
            var pk = CoreAdapter.GetBoxSlot(sav, b, i);
            if (!CoreAdapter.IsEmpty(pk))
                list.Add(new BatchTarget(b, i, pk, $"{CoreAdapter.GetBoxName(sav, b)} · {i + 1}"));
        }
        void AddParty()
        {
            for (int i = 0; i < sav.PartyCount; i++)
                if (CoreAdapter.GetPartySlot(sav, i) is { Species: > 0 } pk)
                    list.Add(new BatchTarget(-1, i, pk, $"Equipe · {i + 1}"));
        }
        void AddBoxes(IEnumerable<int> boxes)
        {
            foreach (var b in boxes)
                for (int i = 0; i < sav.BoxSlotCount; i++)
                    AddBox(b, i);
        }
        switch (scope)
        {
            case BatchScope.CurrentBox: AddBoxes([Boxes.CurrentBox]); break;
            case BatchScope.AllBoxes: AddBoxes(Enumerable.Range(0, sav.BoxCount)); break;
            case BatchScope.Party: AddParty(); break;
            case BatchScope.BoxesAndParty: AddParty(); AddBoxes(Enumerable.Range(0, sav.BoxCount)); break;
            case BatchScope.Marked:
                foreach (var k in _marks.Where(k => k.Bank is null && !k.Other).OrderBy(k => k.Box).ThenBy(k => k.Slot))
                    AddBox(k.Box, k.Slot);
                break;
            case BatchScope.SearchResults:
                var path = _activeTab is { } t ? Normalize(t.Path) : null;
                foreach (var e in Search.Results.Select(r => r.Entry).Where(e => e.Source.IsOpen && path is not null && Normalize(e.Source.Id) == path))
                {
                    if (e.Box < 0)
                    {
                        if (CoreAdapter.GetPartySlot(sav, e.Slot) is { Species: > 0 } pk)
                            list.Add(new BatchTarget(-1, e.Slot, pk, $"Equipe · {e.Slot + 1}"));
                    }
                    else if (e.Box < sav.BoxCount)
                        AddBox(e.Box, e.Slot);
                }
                break;
        }
        return list;

        static string Normalize(string p) => FullPath(p);
    }

    /// <summary>Grava o lote: pergunta antes, pula (no modo legal) os que ficariam ilegais e registra um passo de desfazer.</summary>
    private async Task ApplyBatchAsync(IReadOnlyList<BatchChange> changes)
    {
        if (_sav is not { } sav || _history is null)
            return;
        var illegal = changes.Where(c => c.BecomesIllegal).ToList();
        var write = LegalMode ? changes.Where(c => !c.BecomesIllegal).ToList() : [.. changes];
        if (write.Count == 0)
        {
            Status = $"Nada gravado: os {illegal.Count} Pokémon ficariam ilegais e o modo legal está ligado. Desligue o modo legal na barra lateral para gravar mesmo assim.";
            return;
        }
        string Line(BatchChange c) => $"{CoreAdapter.SpeciesNames[c.Result.Species]} ({c.Target.Where})" + (c.BecomesIllegal ? " ⚠" : "");
        var message = $"{write.Count} Pokémon serão alterados neste save. Dá para desfazer com Ctrl+Z.";
        if (illegal.Count > 0)
            message += LegalMode
                ? $" Modo legal: {illegal.Count} que ficariam ilegais serão pulados."
                : $" Atenção: {illegal.Count} vão ficar ilegais (⚠).";
        if (!await ConfirmAsync("Aplicar edição em lote", message, "Aplicar", details: [.. write.Select(Line)], icon: "⚙"))
        {
            Status = "Edição em lote cancelada.";
            return;
        }
        _history.Record($"edição em lote ({write.Count})", [.. write.Select(c => SlotHistory.KeyOf(c.Target.Box, c.Target.Slot))]);
        foreach (var c in write)
        {
            if (c.Target.Box < 0)
                CoreAdapter.SetPartySlot(sav, c.Result, c.Target.Slot);
            else
                CoreAdapter.SetBoxSlot(sav, c.Result, c.Target.Box, c.Target.Slot);
        }
        IsDirty = true;
        RefreshSlots();
        OnHistoryChanged();
        Status = $"Edição em lote: {write.Count} Pokémon alterados" + (LegalMode && illegal.Count > 0 ? $", {illegal.Count} pulados (ficariam ilegais)." : ".")
            + " Ctrl+Z desfaz. Lembre-se de salvar.";
    }

    private void RefreshSlots()
    {
        if (_sav is null)
            return;
        Boxes.Reload();
        Party.Load(_sav);
        Editor = null; // o slot editado pode ter mudado de lugar
        _selectedSlot = null;
        ClearMarks(); // as posicoes marcadas podem ter outro Pokemon agora
        RaiseSelectionChanged();
    }

    /// <summary>Selecionar um slot pela interface: pergunta antes de descartar edicoes nao aplicadas.</summary>
    public async Task SelectSlotAsync(SlotViewModel slot)
    {
        if (TouchSelectionMode && !slot.IsParty) { ToggleMark(slot, false); return; }
        ClearMarks(); // clique simples desfaz a selecao multipla
        if (slot.IsBank || slot.IsOther)
        {
            // No bank (e no outro save) so destaca (o editor fica nas paginas do save); Excluir e Exportar PKM funcionam no selecionado.
            if (_selectedSlot is not null)
                _selectedSlot.IsSelected = false;
            _selectedSlot = slot;
            slot.IsSelected = true;
            RaiseSelectionChanged();
            Bank.ShowVariants(slot);
            InspectBank(slot);
            return;
        }
        if (slot != _selectedSlot && !await ConfirmDiscardEditAsync())
            return;
        SelectSlot(slot);
        await OfferLegalizeAsync(slot);
    }

    // Pokemon ilegais que o usuario preferiu so abrir: nao pergunta de novo nesta sessao.
    private readonly HashSet<string> _legalizeDeclined = [];

    /// <summary>Clique num Pokemon ilegal: pergunta se quer legalizar; sim legaliza e grava no slot (com Ctrl+Z).</summary>
    private async Task OfferLegalizeAsync(SlotViewModel slot)
    {
        if (!Settings.AskLegalizeOnClick || Editor is not { ShowIllegal: true } editor || slot.IsEmpty)
            return;
        var key = $"{_activeTab?.Path}|{slot.Location}|{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(editor.CurrentData))}";
        if (_legalizeDeclined.Contains(key))
            return;
        var reason = editor.LegalityIssues.Count > 0 ? editor.LegalityIssues[0].TrimEnd('.') : "Ilegal";
        if (!await editor.PreviewLegalizeAsync(true,
                $"{editor.SpeciesName} ({slot.Location}): {reason}. " + Loc.T("Confira o que vai mudar antes de legalizar.")))
            _legalizeDeclined.Add(key);
    }

    /// <summary>true = pode descartar (sem edicoes pendentes ou o usuario confirmou).</summary>
    private async Task<bool> ConfirmDiscardEditAsync()
    {
        if (Editor is not { IsModified: true } editor)
            return true;
        return await ConfirmAsync("Descartar edição?",
            $"As alterações em {editor.SpeciesName} ({editor.Location}) ainda não foram aplicadas.",
            "Descartar", "Continuar editando", isDanger: true);
    }

    /// <summary>
    /// "Usar" no banco de encontros/eventos: gera o Pokemon e abre no editor, no slot vazio selecionado
    /// ou no primeiro slot vazio da caixa atual. So grava ao clicar em Aplicar.
    /// </summary>
    private async void UseEncounter(IEncounterInfo enc)
    {
        if (_sav is null)
            return;
        var pk = EncounterDatabase.ToEntity(_sav, enc, out var error);
        if (pk is null)
        {
            Status = $"Não foi possível gerar este Pokémon para o save: {error}";
            return;
        }
        var slot = _selectedSlot is { IsEmpty: true, IsParty: false } s ? s : Boxes.Slots.FirstOrDefault(x => x.IsEmpty);
        if (slot is null)
        {
            Status = "Esta caixa está cheia. Escolha uma caixa com espaço (ou selecione um slot vazio) e clique em Usar de novo.";
            return;
        }
        if (!await ConfirmDiscardEditAsync())
            return;
        CurrentPage = Boxes;
        SelectSlot(slot, pk);
        Status = $"{CoreAdapter.SpeciesNames[pk.Species]} gerado do banco em {slot.Location}. Confira no editor e clique em Aplicar para gravar.";
    }

    /// <summary>
    /// "Abrir no editor do save" de uma variante do bank: a versao daquele jogo vai para o editor no lugar da copia
    /// anexada (se ela estiver no save aberto) ou num slot vazio da caixa atual. Ao Aplicar, o anexado passa a
    /// apontar para este save.
    /// </summary>
    private async Task UseVariantAsync(PKM original, BankVariant variant)
    {
        if (_sav is null)
        {
            Status = "Abra um save para levar a variante.";
            return;
        }
        var pk = CoreAdapter.ConvertForSave(_sav, variant.Pk, out var error);
        if (pk is null)
        {
            Status = $"Esta variante não cabe em {CoreAdapter.GetGameName(_sav)}: {error}";
            return;
        }
        if (!await ConfirmDiscardEditAsync())
            return;

        // A copia anexada ja esta neste save: a variante entra no lugar dela.
        if (!await ConfirmTransferAsync([(variant.Pk, pk)])) return;
        var id = BankLinks.IdOf(original);
        SlotViewModel? target = null;
        string where;
        var copy = EntitySearch.ReadAll(_sav).FirstOrDefault(e => !CoreAdapter.IsEmpty(e.Pkm) && BankLinks.IdOf(e.Pkm) == id);
        if (copy is not null)
        {
            if (copy.Box < 0)
            {
                CurrentPage = Party;
                target = copy.Slot < Party.Slots.Count ? Party.Slots[copy.Slot] : null;
            }
            else
            {
                CurrentPage = Boxes;
                Boxes.CurrentBox = copy.Box;
                target = copy.Slot < Boxes.Slots.Count ? Boxes.Slots[copy.Slot] : null;
            }
            where = "no lugar da cópia anexada";
        }
        else
        {
            target = Boxes.Slots.FirstOrDefault(x => x.IsEmpty);
            if (target is not null)
                CurrentPage = Boxes;
            where = "num slot vazio";
        }
        if (target is null)
        {
            Status = "A caixa atual está cheia. Escolha uma caixa com espaço e tente de novo.";
            return;
        }
        var savePath = CurrentSavePath;
        var saveName = SaveLabel;
        SelectSlot(target, pk, _ => BankLinks.Relink(original, savePath, saveName));
        var converted = variant.Pk.GetType() == _sav.PKMType ? "" : $" (convertida de {variant.Pk.Extension.ToUpperInvariant()})";
        Status = $"Variante {variant.Pk.Extension.ToUpperInvariant()} de {CoreAdapter.SpeciesNames[pk.Species]}{converted} aberta {where}, em {target.Location}. Confira e clique em Aplicar: o anexado passa a apontar para este save.";
    }

    private void SelectSlot(SlotViewModel slot) => SelectSlot(slot, null);

    /// <param name="generated">Pokemon vindo de fora (banco) para abrir no editor no lugar do conteudo do slot.</param>
    /// <param name="applied">Chamado depois de gravar no slot (ex.: religar um anexado).</param>
    private void SelectSlot(SlotViewModel slot, PKM? generated, Action<PKM>? applied = null)
    {
        if (_selectedSlot is not null)
            _selectedSlot.IsSelected = false;
        _selectedSlot = slot;
        slot.IsSelected = true;
        RaiseSelectionChanged();
        if (_sav is null || slot.Pkm is null)
            return;

        // Slot vazio: abre o editor com um Pokemon em branco para permitir criar/colar Showdown.
        var source = generated ?? (slot.IsEmpty ? CoreAdapter.CreateBlank(_sav) : slot.Pkm);
        var tab = Editor?.SelectedTab ?? 0;
        PokemonEditorViewModel? nextEditor = null;
        nextEditor = new PokemonEditorViewModel(source, slot.Location, pk =>
        {
            if (CoreAdapter.IsEmpty(pk))
            {
                Status = "Escolha uma espécie antes de aplicar.";
                return;
            }
            _history!.Record($"editar {CoreAdapter.SpeciesNames[pk.Species]}", SlotHistory.KeyOf(slot.Box, slot.Slot));
            OnHistoryChanged();
            IsDirty = true;
            slot.Write(_sav, pk);
            if (slot.IsParty)
                Party.Load(_sav);
            applied?.Invoke(pk);
            RaiseSelectionChanged();
            Status = $"{CoreAdapter.SpeciesNames[pk.Species]} gravado em {slot.Location}. Lembre-se de exportar o save.";
        }, s => Status = s, isNew: generated is null && slot.IsEmpty, pendingApply: generated is not null, sav: _sav, legalMode: Settings.LegalMode) { SelectedTab = tab, ShowQr = ShowPokemonQr, Confirm = (t, m, ok) => ConfirmAsync(t, m, ok),
            ConfirmPreview = ConfirmEditorPreviewAsync, IsCurrentEditor = () => Editor == nextEditor };
        Editor = nextEditor;
    }
}

/// <summary>Um resultado da busca global.</summary>
public sealed class SearchHitViewModel(StoredEntity entity, string reason, string boxName)
{
    public StoredEntity Entity { get; } = entity;
    public string Title => Entity.Pkm.IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[Entity.Pkm.Species]
        + (Entity.Pkm.IsNicknamed ? $" ({Entity.Pkm.Nickname})" : "");
    public string Location => Entity.Box < 0 ? $"Equipe · {Entity.Slot + 1}" : $"{boxName} · {Entity.Slot + 1}";
    public string Reason { get; } = reason;
    public bool IsShiny => Entity.Pkm.IsShiny;

    private Avalonia.Media.Imaging.Bitmap? _sprite;
    public Avalonia.Media.Imaging.Bitmap? Sprite => _sprite ??= SpriteService.GetSprite(Entity.Pkm);
}

/// <summary>Bolinha de cor na barra lateral.</summary>
public sealed class ThemeOptionViewModel(Theme.AppThemePreset preset, bool selected, RelayCommand select) : ViewModelBase
{
    public Theme.AppThemePreset Preset { get; } = preset;
    private bool _isSelected = selected;
    public bool IsSelected { get => _isSelected; set { if (Set(ref _isSelected, value)) Raise(nameof(Header)); } }
    public string Header => (IsSelected ? "✓  " : "     ") + Loc.T(Preset.Name);
    public string Tip => Loc.T(Preset.Description);
    public Avalonia.Media.Imaging.RenderTargetBitmap Preview => Theme.ThemePreview.Get(Preset, App.Current?.RequestedThemeVariant != Avalonia.Styling.ThemeVariant.Light);
    public void RefreshPreview() => Raise(nameof(Preview));
    public RelayCommand SelectCommand { get; } = select;
}

public sealed class FontOptionViewModel(Theme.AppFontChoice font, bool selected, RelayCommand select) : ViewModelBase
{
    public Theme.AppFontChoice Font { get; } = font;
    private bool _isSelected = selected;
    public bool IsSelected { get => _isSelected; set { if (Set(ref _isSelected, value)) Raise(nameof(Header)); } }
    public string Header => (IsSelected ? "✓  " : "     ") + Loc.T(Font.Name);
    public string Tip => Loc.T(Font.Description);
    /// <summary>Amostra do nome escrita na propria fonte (no menu).</summary>
    public Avalonia.Media.FontFamily Family => Avalonia.Media.FontFamily.Parse(Font.Family ?? Theme.AppTheme.Current.FontFamily ?? "fonts:Inter#Inter, $Default");
    /// <summary>"Do tema" muda de amostra quando o tema muda.</summary>
    public void RefreshFamily() => Raise(nameof(Family));
    public RelayCommand SelectCommand { get; } = select;
}

public sealed class LanguageOptionViewModel(string code, string name, bool selected, RelayCommand select) : ViewModelBase
{
    public string Code { get; } = code;
    public string Name { get; } = name;
    private bool _isSelected = selected;
    public bool IsSelected { get => _isSelected; set { if (Set(ref _isSelected, value)) Raise(nameof(Header)); } }
    public string Header => (IsSelected ? "✓  " : "     ") + Name;
    public RelayCommand SelectCommand { get; } = select;
}

public sealed class AccentOptionViewModel(Theme.AccentPreset preset) : ViewModelBase
{
    public Theme.AccentPreset Preset { get; } = preset;
    public Avalonia.Media.IBrush Brush => Preset.Brush;
    public string Name => Preset.Name;
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>O que acontece ao soltar um slot em outro.</summary>
public enum DropMode
{
    /// <summary>Move (troca se o destino estiver ocupado).</summary>
    Move,
    /// <summary>Copia (Ctrl ou Shift).</summary>
    Copy,
    /// <summary>Sobrescreve o destino e esvazia a origem (Alt).</summary>
    Overwrite,
}
