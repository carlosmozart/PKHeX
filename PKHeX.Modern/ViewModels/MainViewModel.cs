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
        SaveManager = new SaveManagerViewModel(Settings, p => _ = OpenAsync(p));
        Encounters = new EncounterDbViewModel(UseEncounter);
        Gifts = new GiftDbViewModel(UseEncounter);
        AllPages = [Boxes, Party, new TrainerPageViewModel(), new BagPageViewModel(s => Status = s), Encounters, Gifts, SaveManager];
        foreach (var page in AllPages)
            page.Changed = () => IsDirty = true;
        Boxes.SlotsLoaded = ApplySearchHighlight;
        Party.SlotsLoaded = ApplySearchHighlight;
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        GoToSearchHitCommand = new RelayCommand(p => { if (p is SearchHitViewModel h) _ = GoToSearchHitAsync(h); });
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RunSearch(); };
        _currentPage = Boxes;
        CheckLegalityCommand = new RelayCommand(CheckLegality, () => HasSave);
        CreateCommand = new RelayCommand(CreateInFirstEmpty, () => HasSave);
        DeleteCommand = new RelayCommand(() => _ = DeleteSelectedAsync(), () => CanExportEntity);
        UndoCommand = new RelayCommand(Undo, () => _history?.CanUndo == true);
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
    public EncounterDbViewModel Encounters { get; }
    public GiftDbViewModel Gifts { get; }
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
            if (value is null || !Set(ref _currentPage, value))
                return;
            Raise(nameof(ShowEditorPanel));
            if (value == SaveManager)
                _ = SaveManager.RefreshAsync();
            else if (value == Gifts)
                _ = Gifts.EnsureLoadedAsync();
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

    public bool HasSave => _sav is not null;
    /// <summary>Painel do editor: some nas paginas de lista (Saves, Encontros, Eventos), que usam a largura toda.</summary>
    public bool ShowEditorPanel => HasSave && CurrentPage != SaveManager && CurrentPage != Encounters && CurrentPage != Gifts;
    public string GameName => _sav is null ? "Nenhum save aberto" : CoreAdapter.GetGameName(_sav);
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
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText = "Cancelar", bool isDanger = false)
    {
        Dialog?.Complete(false); // so uma pergunta por vez
        var dialog = new ConfirmDialogViewModel(title, message, confirmText, cancelText, isDanger);
        Dialog = dialog;
        try { return await dialog.Result; }
        finally { if (Dialog == dialog) Dialog = null; }
    }

    private bool _isDirty;
    /// <summary>Ha alteracoes no save que ainda nao foram exportadas.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            Set(ref _isDirty, value);
            if (value)
                InvalidateSearch(); // algo no save mudou
        }
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
        OnHistoryChanged();
        Editor = null;
        foreach (var page in AllPages)
            page.Load(sav);
        CurrentPage = Boxes;
        foreach (var p in (string[])[nameof(HasSave), nameof(ShowEditorPanel), nameof(GameName), nameof(TrainerInfo), nameof(Pages)])
            Raise(p);
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
            CoreAdapter.ExportSave(_sav, path);
            IsDirty = false;
            Status = $"Salvo em {path}";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao salvar: {ex.Message}";
        }
    }

    public string? SuggestedFileName => _sav?.Metadata.FileName;

    /// <summary>Arrastar e soltar: move/troca (ou copia, com Ctrl) entre slots de caixa e equipe.</summary>
    public async Task MoveSlotAsync(SlotViewModel src, SlotViewModel dst, bool copy)
    {
        if (_sav is null || src == dst)
            return;
        if (copy && !dst.IsEmpty && !src.IsEmpty && !await ConfirmAsync("Substituir Pokémon?",
                $"{dst.Title} ({dst.Location}) será substituído por uma cópia de {src.Title}. Dá para desfazer com Ctrl+Z.",
                "Substituir"))
            return;
        MoveSlot(src, dst, copy);
    }

    public void MoveSlot(SlotViewModel src, SlotViewModel dst, bool copy)
    {
        if (_sav is null)
            return;
        var srcName = src.Title;
        _history!.Record(copy ? $"copiar {srcName}" : $"mover {srcName}",
            SlotHistory.KeyOf(src.Box, src.Slot), SlotHistory.KeyOf(dst.Box, dst.Slot));
        var error = CoreAdapter.MoveSlot(_sav,
            CoreAdapter.GetSlotInfo(_sav, src.Box, src.Slot),
            CoreAdapter.GetSlotInfo(_sav, dst.Box, dst.Slot), copy);
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
        Status = copy
            ? $"{srcName} copiado para {dst.Location}. Lembre-se de exportar o save."
            : $"{srcName} movido para {dst.Location}. Lembre-se de exportar o save.";
    }

    /// <summary>Soltar um arquivo .pk* sobre um slot (pergunta antes de substituir um Pokemon).</summary>
    public async Task ImportFileAsync(SlotViewModel dst, string path)
    {
        if (_sav is null)
            return;
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
        ImportEntity(dst, pk);
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
        if (_sav is null || _selectedSlot is not { IsEmpty: false } slot)
            return;
        var name = slot.Title;
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
    private void CheckLegality()
    {
        var slots = Boxes.Slots.Concat(Party.Slots).Where(s => !s.IsEmpty).ToList();
        var bad = slots.Where(s => s.IsLegal == false).ToList();
        Status = bad.Count == 0
            ? $"Todos os {slots.Count} Pokémon da caixa e da equipe são legais."
            : $"{bad.Count} de {slots.Count} com problema: " + string.Join(", ", bad.Select(s => $"{s.Title} ({s.Location})"));
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
    }

    private void RefreshSlots()
    {
        if (_sav is null)
            return;
        Boxes.Reload();
        Party.Load(_sav);
        Editor = null; // o slot editado pode ter mudado de lugar
        _selectedSlot = null;
        RaiseSelectionChanged();
    }

    /// <summary>Selecionar um slot pela interface: pergunta antes de descartar edicoes nao aplicadas.</summary>
    public async Task SelectSlotAsync(SlotViewModel slot)
    {
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
        }, s => Status = s, isNew: generated is null && slot.IsEmpty, pendingApply: generated is not null, sav: _sav) { SelectedTab = tab };
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
