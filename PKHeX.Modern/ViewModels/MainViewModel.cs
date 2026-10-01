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

    public MainViewModel()
    {
        CoreAdapter.SetLanguage("en");
        ToggleThemeCommand = new RelayCommand(App.ToggleTheme);
        Boxes = new BoxesPageViewModel(SelectSlot);
        Party = new PartyPageViewModel(SelectSlot);
        // Registro de paginas: a ordem aqui e a ordem na barra lateral.
        AllPages = [Boxes, Party, new TrainerPageViewModel(), new BagPageViewModel(s => Status = s)];
        _currentPage = Boxes;
    }

    public BoxesPageViewModel Boxes { get; }
    public PartyPageViewModel Party { get; }
    private IReadOnlyList<PageViewModel> AllPages { get; }
    public IEnumerable<PageViewModel> Pages => AllPages.Where(p => p.IsAvailable);

    private PageViewModel _currentPage;
    public PageViewModel CurrentPage { get => _currentPage; set { if (value is not null) Set(ref _currentPage, value); } }

    public RelayCommand ToggleThemeCommand { get; }

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
        foreach (var p in (string[])[nameof(HasSave), nameof(GameName), nameof(TrainerInfo), nameof(Pages)])
            Raise(p);
        Status = $"Aberto: {System.IO.Path.GetFileName(path)}";
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

    private void SelectSlot(SlotViewModel slot)
    {
        if (_selectedSlot is not null)
            _selectedSlot.IsSelected = false;
        _selectedSlot = slot;
        slot.IsSelected = true;
        if (_sav is null || slot.Pkm is null)
            return;

        // Slot vazio: abre o editor com um Pokemon em branco para permitir criar/colar Showdown.
        var source = slot.IsEmpty ? CoreAdapter.CreateBlank(_sav) : slot.Pkm;
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
            Status = $"{CoreAdapter.SpeciesNames[pk.Species]} gravado em {slot.Location}. Lembre-se de exportar o save.";
        }, s => Status = s);
    }
}
