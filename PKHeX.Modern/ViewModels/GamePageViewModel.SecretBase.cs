using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    public bool HasSecretBase => _sav is SAV3RS or SAV3E or SAV6AO;
    public bool IsSecretBaseTab => Tab == 14;
    public SecretBase3EditorViewModel? SecretBase3 { get; private set; }
    public SecretBase6EditorViewModel? SecretBase6 { get; private set; }
    private void RefreshSecretBase()
    {
        SecretBase3 = _sav is SAV3 { LargeBlock: ISaveBlock3LargeHoenn } s3 ? new(s3, Edit) : null;
        SecretBase6 = _sav is SAV6AO ao ? new(ao, Edit, _confirm) : null;
        foreach (var p in (string[])[nameof(SecretBase3), nameof(SecretBase6), nameof(HasSecretBase)]) Raise(p);
    }
}

/// <summary>
/// Bases secretas da Gen 3 (Ruby/Sapphire/Emerald): a sua e as recebidas de outros jogadores. Edita o dono (nome, gênero,
/// TID/SID, visitas, batalha de hoje, registro) e mostra a equipe que defende a base.
/// </summary>
public sealed class SecretBase3EditorViewModel : ViewModelBase
{
    private readonly SAV3 _sav;
    private readonly Action<string, Action> _edit;

    public SecretBase3EditorViewModel(SAV3 sav, Action<string, Action> edit)
    {
        _sav = sav; _edit = edit;
        Reload();
    }

    public IReadOnlyList<SecretBase3RowViewModel> Bases { get; private set; } = [];
    public bool HasBases => Bases.Count > 0;
    public string Summary => Bases.Count == 0 ? "Nenhuma base secreta neste save (o jogo cria a sua ao usar Secret Power)." : $"{Bases.Count} base(s) secreta(s). A sua tem o mesmo TID/SID do save; as outras vieram de outros jogadores por Record Mixing.";

    private SecretBase3RowViewModel? _selected;
    public SecretBase3RowViewModel? Selected { get => _selected; set { if (Set(ref _selected, value)) Raise(nameof(HasSelection)); } }
    public bool HasSelection => _selected is not null;

    private void Reload()
    {
        var manager = ((ISaveBlock3LargeHoenn)_sav.LargeBlock).SecretBases;
        Bases = [.. manager.Bases.Select((b, i) => new SecretBase3RowViewModel(b, b.TID16 == _sav.TID16 && b.SID16 == _sav.SID16, _edit))];
        Selected = Bases.FirstOrDefault();
        Raise(nameof(Bases)); Raise(nameof(HasBases)); Raise(nameof(Summary));
    }
}

public sealed class SecretBase3RowViewModel(SecretBase3 secret, bool own, Action<string, Action> edit) : ViewModelBase
{
    public string Title => own ? $"🏠 {secret.OriginalTrainerName}" : secret.OriginalTrainerName;
    public string Subtitle => $"{(own ? "Sua base" : "Outro jogador")} · {secret.OriginalTrainerClassName} · local {secret.SecretBaseLocation}";
    public string Name
    {
        get => secret.OriginalTrainerName;
        set { value = (value ?? "").Trim(); if (value.Length is 0 or > 7 || value == Name) return; edit("Nome do dono da base", () => secret.OriginalTrainerName = value); Raise(); Raise(nameof(Title)); }
    }
    public IReadOnlyList<string> Genders { get; } = ["♂ Masculino", "♀ Feminino"];
    public int Gender { get => secret.OriginalTrainerGender & 1; set { if (value is < 0 or > 1 || value == Gender) return; edit("Gênero do dono da base", () => secret.OriginalTrainerGender = (byte)value); Raise(); } }
    public int TID { get => secret.TID16; set { if (value is < 0 or > 65535 || value == TID) return; edit("TID do dono da base", () => secret.TID16 = (ushort)value); Raise(); } }
    public int SID { get => secret.SID16; set { if (value is < 0 or > 65535 || value == SID) return; edit("SID do dono da base", () => secret.SID16 = (ushort)value); Raise(); } }
    public int TimesEntered { get => secret.TimesEntered; set { if (value is < 0 or > 255 || value == TimesEntered) return; edit("Visitas à base", () => secret.TimesEntered = (byte)value); Raise(); } }
    public bool BattledToday { get => secret.BattledToday; set { if (value == BattledToday) return; edit("Batalha de hoje na base", () => secret.BattledToday = value); Raise(); } }
    public bool Registered { get => secret.RegistryStatus == 1; set { if (value == Registered) return; edit("Base registrada", () => secret.RegistryStatus = value ? 1 : 0); Raise(); } }
    /// <summary>A equipe que o dono usa na batalha da base (espécie, nível, item, golpes).</summary>
    public IReadOnlyList<string> Team => [.. secret.Team.Team.Where(p => p.Species != 0).Select(p =>
    {
        var s = GameInfo.Strings;
        var moves = new[] { p.Move1, p.Move2, p.Move3, p.Move4 }.Where(m => m != 0).Select(m => s.movelist[m]);
        var item = p.HeldItem != 0 ? $" @ {s.GetItemStrings(EntityContext.Gen3)[p.HeldItem]}" : "";
        return $"{s.specieslist[p.Species]} Nv. {p.Level}{item} · {string.Join(", ", moves)}";
    })];
    public bool HasTeam => Team.Count > 0;
}

/// <summary>
/// Bases secretas de Omega Ruby/Alpha Sapphire: textos e números da sua base, as bases de outros jogadores (apagar) e
/// todas as decorações (Goods) no estoque.
/// </summary>
public sealed class SecretBase6EditorViewModel : ViewModelBase
{
    private readonly SAV6AO _sav;
    private readonly Action<string, Action> _edit;
    private readonly Func<string, string, string, System.Threading.Tasks.Task<bool>> _confirm;

    public SecretBase6EditorViewModel(SAV6AO sav, Action<string, Action> edit, Func<string, string, string, System.Threading.Tasks.Task<bool>> confirm)
    {
        _sav = sav; _edit = edit; _confirm = confirm;
        LoadOthers();
    }

    private SecretBase6 Self => _sav.SecretBase.GetSecretBaseSelf();
    public bool HasOwnBase => !Self.IsEmpty;
    public string OwnSummary => HasOwnBase ? $"Sua base fica no local {Self.BaseLocation}." : "Você ainda não montou uma base secreta (os textos só aparecem no jogo depois de montar).";

    public string TrainerName { get => Self.TrainerName; set => SetText("Nome na base", value, 12, Self.TrainerName, v => { var b = Self; b.TrainerName = v; }); }
    public string TeamName { get => Self.TeamName; set => SetText("Nome da equipe da base", value, 16, Self.TeamName, v => { var b = Self; b.TeamName = v; }); }
    public string TeamSlogan { get => Self.TeamSlogan; set => SetText("Lema da base", value, 16, Self.TeamSlogan, v => { var b = Self; b.TeamSlogan = v; }); }
    public string SayHappy { get => Self.SayHappy; set => SetText("Frase de alegria", value, 16, Self.SayHappy, v => { var b = Self; b.SayHappy = v; }); }
    public string SayEncourage { get => Self.SayEncourage; set => SetText("Frase de incentivo", value, 16, Self.SayEncourage, v => { var b = Self; b.SayEncourage = v; }); }
    public string SayBlackboard { get => Self.SayBlackboard; set => SetText("Frase do quadro", value, 16, Self.SayBlackboard, v => { var b = Self; b.SayBlackboard = v; }); }
    public string SayConfettiBall { get => Self.SayConfettiBall; set => SetText("Frase dos confetes", value, 16, Self.SayConfettiBall, v => { var b = Self; b.SayConfettiBall = v; }); }

    private void SetText(string description, string? value, int max, string current, Action<string> apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        value ??= "";
        if (value.Length > max) value = value[..max];
        if (value == current) return;
        _edit(description, () => apply(value));
        Raise(name);
    }

    public IReadOnlyList<string> Ranks { get; } = ["Sem nível", "Bronze", "Prata", "Ouro", "Platina"];
    public int Rank { get => Math.Clamp((int)Self.Rank, 0, 4); set { if (value is < 0 or > 4 || value == Rank) return; _edit("Nível da base", () => { var b = Self; b.Rank = (SecretBase6Rank)value; }); Raise(); } }
    public long FlagsFromFriends { get => Self.TotalFlagsFromFriends; set { value = Math.Clamp(value, 0, 99999); if (value == FlagsFromFriends) return; _edit("Bandeiras de amigos", () => { var b = Self; b.TotalFlagsFromFriends = (uint)value; }); Raise(); } }
    public long FlagsFromOthers { get => Self.TotalFlagsFromOther; set { value = Math.Clamp(value, 0, 99999); if (value == FlagsFromOthers) return; _edit("Bandeiras de outros", () => { var b = Self; b.TotalFlagsFromOther = (uint)value; }); Raise(); } }

    public RelayCommand GiveAllGoodsCommand => new(() => { _edit("Todas as decorações da base", () => _sav.SecretBase.GiveAllGoods()); Raise(nameof(GoodsNote)); });
    public string GoodsNote => "Dá todas as decorações (Goods) no estoque, como o botão do PKHeX. A arrumação da base não muda.";

    public IReadOnlyList<SecretBase6OtherViewModel> Others { get; private set; } = [];
    public bool HasOthers => Others.Count > 0;
    private void LoadOthers()
    {
        var block = _sav.SecretBase;
        Others = [.. Enumerable.Range(0, SecretBase6Block.OtherSecretBaseCount)
            .Select(i => (Index: i, Base: block.GetSecretBaseOther(i)))
            .Where(x => !x.Base.IsEmpty)
            .Select(x => new SecretBase6OtherViewModel($"{x.Base.TrainerName} · {x.Base.TeamName}".Trim(' ', '·'), $"local {x.Base.BaseLocation}", new RelayCommand(() => _ = DeleteAsync(x.Index, x.Base.TrainerName))))];
        Raise(nameof(Others)); Raise(nameof(HasOthers));
    }

    private async System.Threading.Tasks.Task DeleteAsync(int index, string name)
    {
        if (!await _confirm("Apagar base?", $"Apagar a base secreta de {name} deste save? Ctrl+Z desfaz.", "Apagar"))
            return;
        _edit($"Apagar base de {name}", () => _sav.SecretBase.DeleteOther(index));
        LoadOthers();
    }
}

public sealed record SecretBase6OtherViewModel(string Title, string Subtitle, RelayCommand DeleteCommand);
