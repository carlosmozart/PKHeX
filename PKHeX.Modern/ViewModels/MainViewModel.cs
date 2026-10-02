using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed class MainViewModel : ViewModelBase
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
        ToggleThemeCommand = new RelayCommand(() =>
        {
            Settings.DarkTheme = App.ToggleTheme();
            Settings.Save();
        });
        Party = new PartyPageViewModel(s => _ = SelectSlotAsync(s));
        Boxes = new BoxesPageViewModel(s => _ = SelectSlotAsync(s)) { Party = Party };
        // Registro de paginas: a ordem aqui e a ordem na barra lateral.
        SaveManager = new SaveManagerViewModel(Settings, p => _ = OpenAsync(p), (t, m, ok) => ConfirmAsync(t, m, ok, isDanger: true), s => Status = s);
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
        Encounters = new EncounterDbViewModel(UseEncounter);
        Gifts = new GiftDbViewModel(UseEncounter);
        Help = new HelpPageViewModel(Settings);
        OpenHelpCommand = new RelayCommand(OpenHelp);
        CloseHelpCommand = new RelayCommand(() => IsHelpOpen = false);
        AllPages = [Boxes, Party, Bank, Pokedex, new TrainerPageViewModel(), new BagPageViewModel(s => Status = s), Encounters, Gifts, SaveManager];
        foreach (var page in AllPages)
            page.Changed = () => IsDirty = true;
        Boxes.SlotsLoaded = () => { ApplySearchHighlight(); ApplyMarks(); };
        Bank.SlotsLoaded = ApplyMarks;
        Bank.Sorted = ClearMarks;
        Boxes.Sort = SortBoxes;
        ClearMarksCommand = new RelayCommand(ClearMarks);
        DeleteMarkedCommand = new RelayCommand(() => _ = DeleteMarkedAsync());
        Party.SlotsLoaded = ApplySearchHighlight;
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        GoToSearchHitCommand = new RelayCommand(p => { if (p is SearchHitViewModel h) _ = GoToSearchHitAsync(h); });
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RunSearch(); };
        _currentPage = Boxes;
        CheckLegalityCommand = new RelayCommand(() => _ = CheckLegalityAsync(), () => HasSave && !_checkingLegality);
        CreateCommand = new RelayCommand(CreateInFirstEmpty, () => HasSave);
        DeleteCommand = new RelayCommand(() => _ = DeleteSelectedAsync(), () => CanExportEntity || HasMarks);
        UndoCommand = new RelayCommand(Undo, () => _history?.CanUndo == true);
        ShowPendingCommand = new RelayCommand(() => _ = ShowPendingAsync());
        RedoCommand = new RelayCommand(Redo, () => _history?.CanRedo == true);
    }

    public RelayCommand CheckLegalityCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RedoCommand { get; }
    public string UndoTip => _history?.UndoDescription is { } d ? $"Desfazer: {d} (Ctrl+Z)" : "Nada para desfazer (Ctrl+Z)";
    public string RedoTip => _history?.RedoDescription is { } d ? $"Refazer: {d} (Ctrl+Y)" : "Nada para refazer (Ctrl+Y)";
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
    }

    /// <summary>F1 / botao "Ajuda e novidades".</summary>
    public void OpenHelp() => IsHelpOpen = true;
    public PartyPageViewModel Party { get; }
    private IReadOnlyList<PageViewModel> AllPages { get; }
    public IReadOnlyList<PageViewModel> Pages
    {
        get
        {
            var pages = AllPages.Where(p => p.IsAvailable).ToList();
            for (int i = 0; i < pages.Count; i++)
                pages[i].Shortcut = i < 9 ? $"Ctrl+{i + 1}" : "";
            return pages;
        }
    }

    // Atalhos de teclado (ligados em MainWindow.OnKeyDown)
    /// <summary>Vai para a pagina de indice <paramref name="index"/> (Ctrl+1..9).</summary>
    public void GoToPage(int index)
    {
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
            if (value == SaveManager)
                _ = SaveManager.RefreshAsync();
            else if (value == Gifts)
                _ = Gifts.EnsureLoadedAsync();
            else if (value == Pokedex)
                _ = Pokedex.RefreshAsync(); // rele sempre: o save aberto e o bank podem ter mudado
        }
    }

    public RelayCommand ToggleThemeCommand { get; }

    // Cor de destaque
    public IReadOnlyList<AccentOptionViewModel> AccentOptions { get; }
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

    public bool HasLastSave => Settings.LastSavePath is { } p && System.IO.File.Exists(p);
    public string LastSaveName => HasLastSave ? System.IO.Path.GetFileName(Settings.LastSavePath!) : "";

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
    /// <summary>Painel do editor: some nas paginas de lista (Saves, Encontros, Eventos), que usam a largura toda.</summary>
    public bool ShowEditorPanel => HasSave && CurrentPage != SaveManager && CurrentPage != Encounters && CurrentPage != Gifts && CurrentPage != Bank && CurrentPage != Pokedex && !IsHelpOpen;
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
        IReadOnlyList<string>? details = null, string icon = "")
    {
        Dialog?.Complete(false); // so uma pergunta por vez
        var dialog = new ConfirmDialogViewModel(title, message, confirmText, cancelText, isDanger, details, icon);
        Dialog = dialog;
        try { return await dialog.Result; }
        finally { if (Dialog == dialog) Dialog = null; }
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
            list.Add("Alterações na mochila ou nos dados do treinador.");
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
            Raise(nameof(PendingActions));
            Raise(nameof(PendingText));
            if (value)
                InvalidateSearch(); // algo no save mudou
            else
                _historyAtSave = _history?.Count ?? 0;
        }
    }

    /// <summary>Fechar o app: pergunta se o save principal ou o outro save tem alteracoes nao gravadas.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!await ConfirmDiscardChangesAsync("Fechar o PKHeX Modern"))
            return false;
        return !OtherSave.IsDirty || await ConfirmAsync("Outro save não salvo",
            $"O outro save ({OtherSave.FileName}, na página Bank) tem alterações que ainda não foram gravadas.", "Fechar sem salvar", "Voltar", isDanger: true);
    }

    /// <summary>Confirma o descarte das alteracoes nao exportadas (true = pode seguir).</summary>
    public async Task<bool> ConfirmDiscardChangesAsync(string action)
    {
        if (!IsDirty)
            return true;
        return await ConfirmAsync("Alterações não exportadas",
            $"O save atual tem alterações que ainda não foram exportadas. {action} vai descartá-las.",
            "Descartar alterações", "Voltar", isDanger: true);
    }

    /// <summary>Abre um save pela interface: pergunta antes de descartar alteracoes.</summary>
    public async Task OpenAsync(string path)
    {
        if (await ConfirmDiscardChangesAsync("Abrir outro save"))
            Open(path);
    }

    public void Open(string path)
    {
        var sav = CoreAdapter.LoadSave(path);
        if (sav is null)
        {
            Status = "Arquivo não reconhecido como save.";
            return;
        }
        _sav = sav;
        IsDirty = false;
        _searchIndex = null;
        SearchText = "";
        _history = new SlotHistory(sav);
        _historyAtSave = 0;
        ClearMarks();
        OtherSave.MainPath = path;
        OnHistoryChanged();
        Editor = null;
        foreach (var page in AllPages)
            page.Load(sav);
        CurrentPage = Boxes;
        foreach (var p in (string[])[nameof(HasSave), nameof(ShowEditorPanel), nameof(GameName), nameof(GameArt), nameof(TrainerInfo), nameof(Pages)])
            Raise(p);
        IsHelpOpen = false;
        RaiseHome();
        RaiseSelectionChanged();
        CheckLegalityCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
        Status = $"Aberto: {System.IO.Path.GetFileName(path)}";
        Settings.LastSavePath = System.IO.Path.GetFullPath(path);
        Settings.Save();
        Raise(nameof(HasLastSave));
        Raise(nameof(LastSaveName));
    }

    public void Export(string path)
    {
        if (_sav is null)
            return;
        try
        {
            var backup = SaveBackup.BeforeOverwrite(path); // copia o arquivo antigo antes de sobrescrever
            CoreAdapter.ExportSave(_sav, path);
            IsDirty = false;
            Status = backup is null
                ? $"Salvo em {path}"
                : $"Salvo em {path}. Backup do arquivo anterior: {System.IO.Path.GetFileName(backup)} (Saves › Backups).";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao salvar: {ex.Message}";
        }
    }

    public string? SuggestedFileName => _sav?.Metadata.FileName;

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
                (converted, legalNote) = await OfferLegalizeAsync(converted, "do bank");
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
                IsDirty = true;
                var legal = legalNote != "" ? " " + legalNote : CoreAdapter.IsLegal(converted) == false ? " Atenção: ficou ilegal depois da conversão; veja o cartão de legalidade." : "";
                Status = $"{name} {(mode == DropMode.Copy ? "copiado" : "trazido")} do bank para {dst.Location}.{legal} Lembre-se de salvar o save.";
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
            // Dentro do outro save: as regras do Core (slots bloqueados etc.).
            if (CoreAdapter.MoveSlot(srcSav, srcInfo, dstInfo, mode == DropMode.Copy, mode == DropMode.Overwrite) is { } error)
            {
                if (error != "")
                    Status = error;
                return;
            }
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
        if (dstSav == _sav)
            (converted, legalNote) = await OfferLegalizeAsync(converted, "do outro save");
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
        if (dstSav != _sav || mode != DropMode.Copy)
            OtherSave.MarkDirty();
        OtherSave.LoadBox();
        RefreshSlots();
        var legal = legalNote != "" ? " " + legalNote : CoreAdapter.IsLegal(converted) == false ? " Atenção: ficou ilegal depois da conversão." : "";
        Status = $"{name} {(mode == DropMode.Copy ? "copiado" : "movido")} para {dst.Location}.{legal} Salve os dois saves para gravar (Ctrl+Z desfaz só o lado do save aberto).";
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
        var legalize = await ConfirmAsync($"{name} está ilegal",
            $"{name} ({origin}) não é legal neste save. Legalizar gera de novo a partir de um encontro real de {CoreAdapter.GetGameName(_sav)}, mantendo natureza, nível, item, apelido e golpes quando possível.",
            "✨ Legalizar", "Trazer como está", details: [.. CoreAdapter.GetLegalityIssues(pk)], icon: "🛡");
        if (!legalize)
            return (pk, $"Atenção: {name} entrou ilegal.");
        return await LegalizeOutsideAsync(pk);
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

    /// <summary>Soltar um arquivo .pk* sobre um slot (pergunta antes de substituir um Pokemon).</summary>
    public async Task ImportFileAsync(SlotViewModel dst, string path)
    {
        if (_sav is null)
            return;
        if (dst is { IsBank: true, BankBox: { } bankBox })
        {
            // No bank o arquivo entra como esta (sem conversao).
            if (PKHeX.Core.FileUtil.GetSupportedFile(path) is not PKM raw)
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
            Status = "Arquivo de Pokémon incompatível com este save.";
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
    public readonly record struct MarkKey(int Box, BankBox? Bank, int Slot);
    private readonly List<MarkKey> _marks = [];
    private MarkKey? _markAnchor;

    private static MarkKey KeyOf(SlotViewModel s) => new(s.Box, s.BankBox, s.Slot);
    private static bool SameBox(MarkKey a, MarkKey b) => a.Box == b.Box && a.Bank == b.Bank;

    public bool HasMarks => _marks.Count > 0;
    public string MarkedText => _marks.Count == 1 ? "1 selecionado" : $"{_marks.Count} selecionados";
    public RelayCommand ClearMarksCommand { get; }
    public RelayCommand DeleteMarkedCommand { get; }

    /// <summary>Ctrl+clique (alterna) ou Shift+clique (marca do ultimo marcado ate este, na mesma caixa).</summary>
    public void ToggleMark(SlotViewModel slot, bool range)
    {
        if (_sav is null || slot.IsParty || slot.IsEmpty || slot.IsOther)
            return;
        var key = KeyOf(slot);
        if (range && _markAnchor is { } anchor && SameBox(anchor, key))
        {
            var slots = slot.IsBank ? Bank.Slots : Boxes.Slots;
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
        var slots = _markAnchor is { Bank: not null } || CurrentPage == Bank && _selectedSlot is { IsBank: true } ? Bank.Slots : Boxes.Slots;
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
        Raise(nameof(MarkedText));
        RaiseSelectionChanged();
    }

    /// <summary>Reaplica o destaque das marcas nos slots visiveis (depois de trocar de caixa).</summary>
    private void ApplyMarks()
    {
        foreach (var s in Boxes.Slots.Concat(Bank.Slots))
            s.IsMarked = _marks.Contains(KeyOf(s));
    }

    /// <summary>Le os Pokemon marcados (na ordem: caixas do save, depois bank; dentro da caixa, por slot).</summary>
    private List<(MarkKey Key, PKM Pkm, string Name)> ResolveMarks()
    {
        var list = new List<(MarkKey, PKM, string)>();
        if (_sav is null)
            return list;
        foreach (var k in _marks.OrderBy(k => k.Bank is not null).ThenBy(k => k.Bank?.Folder).ThenBy(k => k.Box).ThenBy(k => k.Slot))
        {
            var pk = k.Bank is { } bank ? BankStorage.ReadSlot(bank, k.Slot) : CoreAdapter.GetBoxSlot(_sav, k.Box, k.Slot);
            if (pk is not null && !CoreAdapter.IsEmpty(pk))
                list.Add((k, pk, pk.IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[pk.Species]));
        }
        return list;
    }

    private string MarkLocation(MarkKey k) => k.Bank is { } b ? $"bank › {b.Name} · {k.Slot + 1}" : $"{CoreAdapter.GetBoxName(_sav!, k.Box)} · {k.Slot + 1}";

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

        var fromSave = items.Where(i => i.Key.Bank is null).ToList();
        int deleted = 0;
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
        if (dst.IsParty || dst.IsOther)
        {
            Status = dst.IsOther
                ? "Para o outro save, arraste um Pokémon por vez."
                : "Vários Pokémon de uma vez só vão para caixas (do save ou do bank). Para a equipe, arraste um por vez.";
            return;
        }
        bool copy = mode == DropMode.Copy;
        var target = KeyOf(dst);
        bool InTarget(MarkKey k) => SameBox(k, target);

        // 1) O que vai: do bank para o save precisa converter; slots bloqueados nao saem.
        var moving = new List<(MarkKey Key, PKM Data, string Name)>();
        var skipped = new List<string>();
        foreach (var (key, pk, name) in ResolveMarks())
        {
            if (!copy && key.Bank is null && !CoreAdapter.CanWriteBoxSlot(_sav, key.Box, key.Slot))
            {
                skipped.Add($"{name} ({MarkLocation(key)}): slot bloqueado pelo jogo");
                continue;
            }
            var data = pk.Clone();
            if (!dst.IsBank && key.Bank is not null)
            {
                if (CoreAdapter.ConvertForSave(_sav, pk, out var err) is not { } converted)
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

        // Do bank para o save: oferece o Legalizar para os que chegam ilegais (uma pergunta para o grupo).
        var legalNote = "";
        var illegal = dst.IsBank ? [] : moving.Select((m, i) => (m, i)).Where(x => x.m.Key.Bank is not null && CoreAdapter.IsLegal(x.m.Data) == false).ToList();
        if (illegal.Count > 0 && LegalMode && await ConfirmAsync($"{illegal.Count} Pokémon ilegais",
                $"Estes Pokémon do bank não são legais em {CoreAdapter.GetGameName(_sav)}. Legalizar gera cada um de novo a partir de um encontro real do jogo, mantendo natureza, nível, item, apelido e golpes quando possível.",
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

        // 2) Onde cabe: slots livres da caixa de destino (os que estao saindo dela contam como livres).
        HashSet<int> freed = copy ? [] : moving.Where(m => InTarget(m.Key)).Select(m => m.Key.Slot).ToHashSet();
        int size = dst.IsBank ? BankStorage.SlotsPerBox : _sav.BoxSlotCount;
        bool Free(int i) => freed.Contains(i) || (dst.IsBank
            ? BankStorage.IsSlotFree(dst.BankBox!, i)
            : CoreAdapter.CanWriteBoxSlot(_sav, dst.Box, i) && CoreAdapter.IsEmpty(CoreAdapter.GetBoxSlot(_sav, dst.Box, i)));
        var slots = Enumerable.Range(0, size).Select(i => (dst.Slot + i) % size).Where(Free).Take(moving.Count).ToList();
        if (slots.Count < moving.Count)
        {
            Status = $"Não cabe: a caixa de destino tem {slots.Count} espaço(s) livre(s) para {moving.Count} Pokémon. Escolha outra caixa ou libere espaço.";
            return;
        }

        // 3) Desfazer: um passo com todos os slots do save envolvidos.
        var keys = new List<SlotHistory.Key>();
        if (!copy)
            keys.AddRange(moving.Where(m => m.Key.Bank is null).Select(m => SlotHistory.KeyOf(m.Key.Box, m.Key.Slot)));
        if (!dst.IsBank)
            keys.AddRange(slots.Select(i => SlotHistory.KeyOf(dst.Box, i)));
        if (keys.Count > 0)
            _history!.Record($"{(copy ? "copiar" : "mover")} {moving.Count} Pokémon", [.. keys]);

        try
        {
            void Clear(MarkKey k)
            {
                if (k.Bank is { } bank)
                    BankStorage.DeleteSlot(bank, k.Slot);
                else
                    CoreAdapter.DeleteSlot(_sav, CoreAdapter.GetSlotInfo(_sav, k.Box, k.Slot));
            }
            // Primeiro esvazia as origens que ficam na propria caixa de destino, depois grava, por ultimo tira das outras origens
            // (assim, se algo falhar no meio, nenhum Pokemon some).
            // (em pasta externa os arquivos seguintes sobem ao apagar: apaga do ultimo para o primeiro)
            if (!copy)
                foreach (var m in moving.Where(m => InTarget(m.Key)).OrderByDescending(m => m.Key.Bank?.ExternalIndex ?? 0).ThenByDescending(m => m.Key.Slot))
                    Clear(m.Key);
            for (int i = 0; i < moving.Count; i++)
            {
                if (dst.IsBank)
                    BankStorage.WriteSlot(dst.BankBox!, slots[i], moving[i].Data);
                else
                    CoreAdapter.ImportToSlot(_sav, CoreAdapter.GetSlotInfo(_sav, dst.Box, slots[i]), moving[i].Data);
            }
            if (!copy)
                foreach (var m in moving.Where(m => !InTarget(m.Key)).OrderByDescending(m => m.Key.Bank?.ExternalIndex ?? 0).ThenByDescending(m => m.Key.Slot))
                    Clear(m.Key);
        }
        catch (Exception ex)
        {
            Status = $"Erro ao mover: {ex.Message}";
        }
        if (keys.Count > 0)
        {
            IsDirty = true;
            OnHistoryChanged();
        }
        Bank.LoadBox();
        RefreshSlots();
        var where = dst.IsBank ? $"bank › {dst.BankBox!.Name}" : CoreAdapter.GetBoxName(_sav, dst.Box);
        Status = $"{moving.Count} Pokémon {(copy ? "copiados" : "movidos")} para {where}."
                 + legalNote
                 + (keys.Count > 0 ? " Lembre-se de salvar o save." : "")
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
        if (_history?.Undo() is not { } what)
            return;
        IsDirty = true;
        OnHistoryChanged();
        RefreshSlots();
        Status = $"Desfeito: {what}.";
    }

    private void Redo()
    {
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
        ClearMarks(); // clique simples desfaz a selecao multipla
        if (slot.IsBank || slot.IsOther)
        {
            // No bank (e no outro save) so destaca (o editor fica nas paginas do save); Excluir e Exportar PKM funcionam no selecionado.
            if (_selectedSlot is not null)
                _selectedSlot.IsSelected = false;
            _selectedSlot = slot;
            slot.IsSelected = true;
            RaiseSelectionChanged();
            return;
        }
        if (slot != _selectedSlot && !await ConfirmDiscardEditAsync())
            return;
        SelectSlot(slot);
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

    private void SelectSlot(SlotViewModel slot) => SelectSlot(slot, null);

    /// <param name="generated">Pokemon vindo de fora (banco) para abrir no editor no lugar do conteudo do slot.</param>
    private void SelectSlot(SlotViewModel slot, PKM? generated)
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
        Editor = new PokemonEditorViewModel(source, slot.Location, pk =>
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
            RaiseSelectionChanged();
            Status = $"{CoreAdapter.SpeciesNames[pk.Species]} gravado em {slot.Location}. Lembre-se de exportar o save.";
        }, s => Status = s, isNew: generated is null && slot.IsEmpty, pendingApply: generated is not null, sav: _sav, legalMode: Settings.LegalMode) { SelectedTab = tab };
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
