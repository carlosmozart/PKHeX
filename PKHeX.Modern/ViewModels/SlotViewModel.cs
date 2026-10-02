using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>Um slot de caixa, da equipe (<see cref="Box"/> = -1) ou do bank local (<see cref="BankBox"/> definido).</summary>
public sealed class SlotViewModel(int box, int slot) : ViewModelBase
{
    public const int Party = -1;

    /// <summary>Slot do bank local: cria um slot ligado a uma caixa do bank (nao a um save).</summary>
    public static SlotViewModel ForBank(BankBox box, string bankName, int slot) => new(-2, slot) { BankBox = box, BankName = bankName };

    /// <summary>Caixa do bank deste slot (null = slot de save).</summary>
    public BankBox? BankBox { get; private init; }
    public string BankName { get; private init; } = "";
    public bool IsBank => BankBox is not null;

    public int Box { get; } = box;
    public int Slot { get; } = slot;
    public bool IsParty => Box == Party;

    private PKM? _pkm;
    public PKM? Pkm { get => _pkm; private set => Set(ref _pkm, value); }

    private Bitmap? _sprite;
    public Bitmap? Sprite { get => _sprite; private set => Set(ref _sprite, value); }

    private bool _isDropTarget;
    /// <summary>Destaque enquanto um Pokemon e arrastado por cima.</summary>
    public bool IsDropTarget { get => _isDropTarget; set => Set(ref _isDropTarget, value); }

    private bool _isMatch;
    /// <summary>Resultado da busca global (borda dourada).</summary>
    public bool IsMatch { get => _isMatch; set => Set(ref _isMatch, value); }

    private bool _isDimmed;
    /// <summary>Busca ativa e este slot nao combina: fica apagado.</summary>
    public bool IsDimmed { get => _isDimmed; set => Set(ref _isDimmed, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public bool IsEmpty => Pkm is null || CoreAdapter.IsEmpty(Pkm);
    public string Title => IsEmpty ? "" : IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[Pkm!.Species];
    public string Subtitle => IsEmpty || IsEgg ? "" : $"Nv. {Pkm!.CurrentLevel}";
    public string Gender => IsEmpty || IsEgg ? "" : CoreAdapter.GetGenderSymbol(Pkm!);
    public bool IsMale => Gender == "♂";
    public bool IsFemale => Gender == "♀";

    private bool? _isLegal;
    /// <summary>Resultado da analise de legalidade (null = vazio ou falhou).</summary>
    public bool? IsLegal { get => _isLegal; private set { Set(ref _isLegal, value); Raise(nameof(IsLegalOk)); Raise(nameof(IsLegalBad)); } }
    public bool IsLegalOk => IsLegal == true;
    public bool IsLegalBad => IsLegal == false;
    private string _summary = "";
    /// <summary>Resumo do hover, igual ao do PKHeX (set + encontro).</summary>
    public string Tooltip => IsEmpty ? "Vazio" : _summary;
    public string Position => $"{Slot + 1:00}";
    public bool IsShiny => !IsEmpty && Pkm!.IsShiny;
    /// <summary>Icone da bola em que foi capturado (canto inferior esquerdo do cartao).</summary>
    public Bitmap? BallSprite => IsEmpty || IsEgg ? null : SpriteService.GetBallSprite(Pkm!.Ball);
    public bool IsEgg => !IsEmpty && Pkm!.IsEgg;
    public string Nickname => IsEmpty ? "" : Pkm!.IsNicknamed ? Pkm.Nickname : "";
    public bool HasNickname => Nickname.Length > 0;
    public string Location => IsBank ? $"bank {BankName} › {BankBox!.Name}, slot {Slot + 1}"
        : IsParty ? $"equipe, posição {Slot + 1}" : $"caixa {Box + 1}, slot {Slot + 1}";

    public void Load(SaveFile sav)
    {
        var pk = IsParty ? CoreAdapter.GetPartySlot(sav, Slot) : CoreAdapter.GetBoxSlot(sav, Box, Slot);
        Pkm = pk;
        (IsLegal, _summary) = CoreAdapter.IsEmpty(pk) ? (null, "") : CoreAdapter.AnalyzeSlot(pk);
        Sprite = IsParty ? SpriteService.GetSprite(pk) : SpriteService.GetSprite(pk, sav, Box, Slot);
        foreach (var p in (string[])[nameof(IsEmpty), nameof(Title), nameof(Subtitle), nameof(Tooltip), nameof(IsShiny), nameof(IsEgg), nameof(Nickname), nameof(HasNickname), nameof(Gender), nameof(IsMale), nameof(IsFemale), nameof(BallSprite)])
            Raise(p);
    }

    /// <summary>Carrega um Pokemon do bank (no formato original dele) neste slot.</summary>
    public void LoadEntity(PKM? pk)
    {
        Pkm = pk;
        (IsLegal, _summary) = pk is null || CoreAdapter.IsEmpty(pk) ? (null, "") : CoreAdapter.AnalyzeSlot(pk);
        Sprite = pk is null ? null : SpriteService.GetSprite(pk);
        foreach (var p in (string[])[nameof(IsEmpty), nameof(Title), nameof(Subtitle), nameof(Tooltip), nameof(IsShiny), nameof(IsEgg), nameof(Nickname), nameof(HasNickname), nameof(Gender), nameof(IsMale), nameof(IsFemale), nameof(BallSprite)])
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
