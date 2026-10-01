using System;
using System.Collections.ObjectModel;
using System.Linq;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private SaveFile? _sav;

    public MainViewModel()
    {
        CoreAdapter.SetLanguage("en");
        PreviousBoxCommand = new RelayCommand(() => CurrentBox--, () => _sav is not null && CurrentBox > 0);
        NextBoxCommand = new RelayCommand(() => CurrentBox++, () => _sav is not null && CurrentBox < _sav.BoxCount - 1);
        ToggleThemeCommand = new RelayCommand(App.ToggleTheme);
        SelectSlotCommand = new RelayCommand(p => { if (p is SlotViewModel s) SelectSlot(s); });
    }

    public ObservableCollection<SlotViewModel> Slots { get; } = [];

    public RelayCommand PreviousBoxCommand { get; }
    public RelayCommand NextBoxCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand SelectSlotCommand { get; }

    public bool HasSave => _sav is not null;
    public string GameName => _sav is null ? "Nenhum save aberto" : CoreAdapter.GetGameName(_sav);
    public string TrainerInfo => _sav is null ? "Arraste um arquivo ou clique em Abrir" : $"{_sav.OT} · TID {_sav.DisplayTID} · {_sav.PlayTimeString}";
    public string ChecksumInfo => _sav is null ? "" : _sav.ChecksumsValid ? "Checksums válidos" : _sav.ChecksumInfo;

    private string _status = "Pronto";
    public string Status { get => _status; set => Set(ref _status, value); }

    private int _currentBox;
    public int CurrentBox
    {
        get => _currentBox;
        set
        {
            if (_sav is null || value < 0 || value >= _sav.BoxCount || !Set(ref _currentBox, value))
                return;
            LoadBox();
        }
    }

    public string BoxName => _sav is null ? "" : CoreAdapter.GetBoxName(_sav, CurrentBox);
    public string BoxLabel => _sav is null ? "" : $"Caixa {CurrentBox + 1} de {_sav.BoxCount}";

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
        _currentBox = 0;
        Editor = null;
        BuildSlots();
        foreach (var p in (string[])[nameof(HasSave), nameof(GameName), nameof(TrainerInfo), nameof(ChecksumInfo), nameof(CurrentBox)])
            Raise(p);
        LoadBox();
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

    private void BuildSlots()
    {
        Slots.Clear();
        if (_sav is null)
            return;
        for (int i = 0; i < _sav.BoxSlotCount; i++)
            Slots.Add(new SlotViewModel(0, i));
    }

    private void LoadBox()
    {
        if (_sav is null)
            return;
        for (int i = 0; i < Slots.Count; i++)
        {
            var s = new SlotViewModel(CurrentBox, i);
            s.Load(_sav);
            Slots[i] = s;
        }
        Raise(nameof(BoxName));
        Raise(nameof(BoxLabel));
        PreviousBoxCommand.NotifyCanExecuteChanged();
        NextBoxCommand.NotifyCanExecuteChanged();
    }

    private void SelectSlot(SlotViewModel slot)
    {
        foreach (var s in Slots.Where(s => s.IsSelected))
            s.IsSelected = false;
        slot.IsSelected = true;
        if (_sav is null || slot.Pkm is null || CoreAdapter.IsEmpty(slot.Pkm))
        {
            Editor = null;
            return;
        }
        Editor = new PokemonEditorViewModel(slot.Pkm, pk =>
        {
            CoreAdapter.SetBoxSlot(_sav, pk, slot.Box, slot.Slot);
            slot.Load(_sav);
            Status = $"{CoreAdapter.SpeciesNames[pk.Species]} gravado na caixa {slot.Box + 1}, slot {slot.Slot + 1}. Lembre-se de exportar o save.";
        });
    }
}
