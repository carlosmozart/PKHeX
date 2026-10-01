using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private SaveFile? _sav;
    private SlotViewModel? _selectedSlot;

    public AppSettings Settings { get; }

    public MainViewModel(AppSettings? settings = null)
    {
        Settings = settings ?? new AppSettings();
        OpenLastCommand = new RelayCommand(() => { if (HasLastSave) Open(Settings.LastSavePath!); });
        CoreAdapter.SetLanguage("en");
        ToggleThemeCommand = new RelayCommand(() =>
        {
            Settings.DarkTheme = App.ToggleTheme();
            Settings.Save();
        });
        Party = new PartyPageViewModel(SelectSlot);
        Boxes = new BoxesPageViewModel(SelectSlot) { Party = Party };
        // Registro de paginas: a ordem aqui e a ordem na barra lateral.
        AllPages = [Boxes, Party, new TrainerPageViewModel(), new BagPageViewModel(s => Status = s)];
        _currentPage = Boxes;
        CheckLegalityCommand = new RelayCommand(CheckLegality, () => HasSave);
        CreateCommand = new RelayCommand(CreateInFirstEmpty, () => HasSave);
    }

    public RelayCommand CheckLegalityCommand { get; }
    public RelayCommand CreateCommand { get; }

    /// <summary>Slot selecionado com Pokemon (para Exportar PKM).</summary>
    public bool CanExportEntity => _selectedSlot is { IsEmpty: false };
    public string? SuggestedEntityFileName => CanExportEntity ? CoreAdapter.GetEntityFileName(_selectedSlot!.Pkm!) : null;
    public IReadOnlyList<string> EntityExtensions => _sav is null ? [] : CoreAdapter.GetEntityExtensions(_sav);

    public BoxesPageViewModel Boxes { get; }
    public PartyPageViewModel Party { get; }
    private IReadOnlyList<PageViewModel> AllPages { get; }
    public IEnumerable<PageViewModel> Pages => AllPages.Where(p => p.IsAvailable);

    private PageViewModel _currentPage;
    public PageViewModel CurrentPage { get => _currentPage; set { if (value is not null) Set(ref _currentPage, value); } }

    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand OpenLastCommand { get; }

    public bool HasLastSave => Settings.LastSavePath is { } p && System.IO.File.Exists(p);
    public string LastSaveName => HasLastSave ? System.IO.Path.GetFileName(Settings.LastSavePath!) : "";

    public bool OpenLastSaveOnStartup
    {
        get => Settings.OpenLastSaveOnStartup;
        set { Settings.OpenLastSaveOnStartup = value; Settings.Save(); Raise(); }
    }

    public bool HasSave => _sav is not null;
    public string GameName => _sav is null ? "Nenhum save aberto" : CoreAdapter.GetGameName(_sav);
    public string TrainerInfo => _sav is null ? "Arraste um arquivo ou clique em Abrir" : $"{_sav.OT} · TID {_sav.DisplayTID}";

    private string _status = "Pronto";
    public string Status { get => _status; set => Set(ref _status, value); }

    private PokemonEditorViewModel? _editor;
    public PokemonEditorViewModel? Editor { get => _editor; private set { Set(ref _editor, value); Raise(nameof(HasEditor)); } }
    public bool HasEditor => Editor is not null;

    public void Open(string path)
    {
        var sav = CoreAdapter.LoadSave(path);
        if (sav is null)
        {
            Status = "Arquivo não reconhecido como save.";
            return;
        }
        _sav = sav;
        Editor = null;
        foreach (var page in AllPages)
            page.Load(sav);
        CurrentPage = Boxes;
        foreach (var p in (string[])[nameof(HasSave), nameof(GameName), nameof(TrainerInfo), nameof(Pages), nameof(CanExportEntity)])
            Raise(p);
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
            Status = $"Salvo em {path}";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao salvar: {ex.Message}";
        }
    }

    public string? SuggestedFileName => _sav?.Metadata.FileName;

    /// <summary>Arrastar e soltar: move/troca (ou copia, com Ctrl) entre slots de caixa e equipe.</summary>
    public void MoveSlot(SlotViewModel src, SlotViewModel dst, bool copy)
    {
        if (_sav is null)
            return;
        var srcName = src.Title;
        var error = CoreAdapter.MoveSlot(_sav,
            CoreAdapter.GetSlotInfo(_sav, src.Box, src.Slot),
            CoreAdapter.GetSlotInfo(_sav, dst.Box, dst.Slot), copy);
        if (error == "")
            return; // nada a fazer
        if (error is not null)
        {
            Status = error;
            return;
        }
        RefreshSlots();
        Status = copy
            ? $"{srcName} copiado para {dst.Location}. Lembre-se de exportar o save."
            : $"{srcName} movido para {dst.Location}. Lembre-se de exportar o save.";
    }

    /// <summary>Soltar um arquivo .pk* sobre um slot.</summary>
    public void ImportFile(SlotViewModel dst, string path)
    {
        if (_sav is null)
            return;
        var pk = CoreAdapter.LoadEntityFile(_sav, path);
        if (pk is null)
        {
            Status = "Arquivo de Pokémon incompatível com este save.";
            return;
        }
        var error = CoreAdapter.ImportToSlot(_sav, CoreAdapter.GetSlotInfo(_sav, dst.Box, dst.Slot), pk);
        if (error is not null)
        {
            Status = error;
            return;
        }
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

    /// <summary>Importar (botao): vai para o slot selecionado ou, sem selecao, para o primeiro slot vazio da caixa.</summary>
    public void ImportFile(string path)
    {
        var dst = _selectedSlot ?? Boxes.Slots.FirstOrDefault(s => s.IsEmpty);
        if (dst is null)
        {
            Status = "Não há slot vazio nesta caixa. Selecione um slot para substituir.";
            return;
        }
        ImportFile(dst, path);
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
    private void CreateInFirstEmpty()
    {
        CurrentPage = Boxes;
        if (Boxes.Slots.FirstOrDefault(s => s.IsEmpty) is not { } slot)
        {
            Status = "Esta caixa está cheia. Escolha outra caixa ou um slot vazio.";
            return;
        }
        SelectSlot(slot);
        Status = $"Novo Pokémon em {slot.Location}. Escolha a espécie (ou cole um set Showdown) e clique em Aplicar.";
    }

    private void RefreshSlots()
    {
        if (_sav is null)
            return;
        Boxes.Reload();
        Party.Load(_sav);
        Editor = null; // o slot editado pode ter mudado de lugar
        _selectedSlot = null;
        Raise(nameof(CanExportEntity));
    }

    private void SelectSlot(SlotViewModel slot)
    {
        if (_selectedSlot is not null)
            _selectedSlot.IsSelected = false;
        _selectedSlot = slot;
        slot.IsSelected = true;
        Raise(nameof(CanExportEntity));
        if (_sav is null || slot.Pkm is null)
            return;

        // Slot vazio: abre o editor com um Pokemon em branco para permitir criar/colar Showdown.
        var source = slot.IsEmpty ? CoreAdapter.CreateBlank(_sav) : slot.Pkm;
        var tab = Editor?.SelectedTab ?? 0;
        Editor = new PokemonEditorViewModel(source, slot.Location, pk =>
        {
            if (CoreAdapter.IsEmpty(pk))
            {
                Status = "Escolha uma espécie antes de aplicar.";
                return;
            }
            slot.Write(_sav, pk);
            if (slot.IsParty)
                Party.Load(_sav);
            Raise(nameof(CanExportEntity));
            Status = $"{CoreAdapter.SpeciesNames[pk.Species]} gravado em {slot.Location}. Lembre-se de exportar o save.";
        }, s => Status = s, isNew: slot.IsEmpty) { SelectedTab = tab };
    }
}
