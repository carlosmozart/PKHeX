using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed class SlotViewModel(int box, int slot) : ViewModelBase
{
    public int Box { get; } = box;
    public int Slot { get; } = slot;

    private PKM? _pkm;
    public PKM? Pkm { get => _pkm; private set => Set(ref _pkm, value); }

    private Bitmap? _sprite;
    public Bitmap? Sprite { get => _sprite; private set => Set(ref _sprite, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public string Tooltip => Pkm is { } pk && !CoreAdapter.IsEmpty(pk)
        ? $"{CoreAdapter.SpeciesNames[pk.Species]} · Nv. {pk.CurrentLevel}"
        : "Vazio";

    public void Load(SaveFile sav)
    {
        var pk = CoreAdapter.GetBoxSlot(sav, Box, Slot);
        Pkm = pk;
        Sprite = SpriteService.GetSprite(pk, sav, Box, Slot);
        Raise(nameof(Tooltip));
    }
}
