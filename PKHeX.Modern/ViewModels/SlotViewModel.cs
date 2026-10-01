using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>Um slot de caixa ou da equipe. <see cref="Box"/> = -1 indica equipe.</summary>
public sealed class SlotViewModel(int box, int slot) : ViewModelBase
{
    public const int Party = -1;

    public int Box { get; } = box;
    public int Slot { get; } = slot;
    public bool IsParty => Box == Party;

    private PKM? _pkm;
    public PKM? Pkm { get => _pkm; private set => Set(ref _pkm, value); }

    private Bitmap? _sprite;
    public Bitmap? Sprite { get => _sprite; private set => Set(ref _sprite, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public bool IsEmpty => Pkm is null || CoreAdapter.IsEmpty(Pkm);
    public string Title => IsEmpty ? "" : IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[Pkm!.Species];
    public string Subtitle => IsEmpty || IsEgg ? "" : $"Nv. {Pkm!.CurrentLevel}";
    public string Tooltip => IsEmpty ? "Vazio" : $"{Title} · {Subtitle}";
    public string Position => $"{Slot + 1:00}";
    public bool IsShiny => !IsEmpty && Pkm!.IsShiny;
    public bool IsEgg => !IsEmpty && Pkm!.IsEgg;
    public string Nickname => IsEmpty ? "" : Pkm!.IsNicknamed ? Pkm.Nickname : "";
    public bool HasNickname => Nickname.Length > 0;
    public string Location => IsParty ? $"equipe, posição {Slot + 1}" : $"caixa {Box + 1}, slot {Slot + 1}";

    public void Load(SaveFile sav)
    {
        var pk = IsParty ? CoreAdapter.GetPartySlot(sav, Slot) : CoreAdapter.GetBoxSlot(sav, Box, Slot);
        Pkm = pk;
        Sprite = IsParty ? SpriteService.GetSprite(pk) : SpriteService.GetSprite(pk, sav, Box, Slot);
        foreach (var p in (string[])[nameof(IsEmpty), nameof(Title), nameof(Subtitle), nameof(Tooltip), nameof(IsShiny), nameof(IsEgg), nameof(Nickname), nameof(HasNickname)])
            Raise(p);
    }

    public void Write(SaveFile sav, PKM pk)
    {
        if (IsParty)
            CoreAdapter.SetPartySlot(sav, pk, Slot);
        else
            CoreAdapter.SetBoxSlot(sav, pk, Box, Slot);
        Load(sav);
    }
}
