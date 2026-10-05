using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class MainViewModel
{
    private bool _touchUi = !App.ShowShortcuts;
    public bool IsTouchUI { get => _touchUi; set { if (Set(ref _touchUi, value)) { Raise(nameof(ShowTouchToolbar)); Raise(nameof(HasDesktopMarks)); } } }
    public bool HasDesktopMarks => HasMarks && !IsTouchUI;
    public bool ShowTouchToolbar => IsTouchUI && (CurrentPage == Boxes || CurrentPage == Bank);
    private bool _touchSelection;
    public bool TouchSelectionMode
    {
        get => _touchSelection;
        set { if (Set(ref _touchSelection, value) && !value) ClearMarks(); }
    }
    public int MarkedCount => _marks.Count;
    public RelayCommand ToggleTouchSelectionCommand { get; private set; } = null!;
    public RelayCommand MoveTouchSelectionCommand { get; private set; } = null!;
    public RelayCommand BankTouchSelectionCommand { get; private set; } = null!;
    public RelayCommand BatchTouchSelectionCommand { get; private set; } = null!;
    public IReadOnlyList<string> TouchDropOptions { get; } = ["Mover", "Copiar", "Sobrescrever"];
    private int _touchDropIndex;
    public int TouchDropIndex { get => _touchDropIndex; set { if (value is >= 0 and <= 2 && Set(ref _touchDropIndex, value)) Raise(nameof(TouchDropTip)); } }
    public DropMode TouchDropMode => TouchDropIndex switch { 1 => DropMode.Copy, 2 => DropMode.Overwrite, _ => DropMode.Move };
    public string TouchDropTip => TouchDropIndex switch
    {
        1 => "Copia o Pokémon para o destino e preserva a origem.",
        2 => "Substitui o Pokémon do destino e esvazia a origem. No save, pode ser desfeito.",
        _ => "Move para o destino; se ocupado, troca os dois Pokémon. No save, pode ser desfeito.",
    };
    public IReadOnlyList<string> SelectionBoxOptions => Boxes.BoxTabs.Select(b => b.Name).ToArray();
    public int SelectionBoxIndex { get; set; }

    private void InitializeTouchCommands()
    {
        ToggleTouchSelectionCommand = new RelayCommand(() => TouchSelectionMode = !TouchSelectionMode);
        MoveTouchSelectionCommand = new RelayCommand(() => _ = MoveTouchSelectionAsync(false));
        BankTouchSelectionCommand = new RelayCommand(() => _ = MoveTouchSelectionAsync(true));
        BatchTouchSelectionCommand = new RelayCommand(() => { Batch.ScopeIndex = (int)BatchScope.Marked; CurrentPage = Batch; });
    }

    private async Task MoveTouchSelectionAsync(bool toBank)
    {
        if (!HasMarks || _sav is null) return;
        SlotViewModel? destination;
        if (toBank)
        {
            if (Bank.CurrentBox is null) Bank.Reload();
            destination = Bank.Slots.FirstOrDefault(s => s.IsEmpty) ?? Bank.Slots.FirstOrDefault();
        }
        else
        {
            Boxes.CurrentBox = System.Math.Clamp(SelectionBoxIndex, 0, _sav.BoxCount - 1);
            destination = Boxes.Slots.FirstOrDefault(s => s.IsEmpty) ?? Boxes.Slots.FirstOrDefault();
        }
        if (destination is not null) await MoveMarkedAsync(destination, TouchDropMode);
    }
}
