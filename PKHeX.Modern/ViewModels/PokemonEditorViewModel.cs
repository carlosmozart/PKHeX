using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Editor de um Pokemon. Trabalha sobre uma copia (Clone) e so grava no save ao clicar em "Aplicar".
/// Para adicionar um novo campo: crie uma propriedade aqui que leia/escreva em <see cref="_pk"/> e um controle na view.
/// </summary>
public sealed class PokemonEditorViewModel : ViewModelBase
{
    private readonly PKM _pk;
    private readonly Action<PKM> _apply;

    public PokemonEditorViewModel(PKM source, Action<PKM> apply)
    {
        _pk = source.Clone();
        _apply = apply;
        Stats =
        [
            new("PS",      () => _pk.IV_HP,  v => _pk.IV_HP = v,  () => _pk.EV_HP,  v => _pk.EV_HP = v),
            new("Ataque",  () => _pk.IV_ATK, v => _pk.IV_ATK = v, () => _pk.EV_ATK, v => _pk.EV_ATK = v),
            new("Defesa",  () => _pk.IV_DEF, v => _pk.IV_DEF = v, () => _pk.EV_DEF, v => _pk.EV_DEF = v),
            new("At. Esp.",() => _pk.IV_SPA, v => _pk.IV_SPA = v, () => _pk.EV_SPA, v => _pk.EV_SPA = v),
            new("Def. Esp.",() => _pk.IV_SPD, v => _pk.IV_SPD = v, () => _pk.EV_SPD, v => _pk.EV_SPD = v),
            new("Veloc.",  () => _pk.IV_SPE, v => _pk.IV_SPE = v, () => _pk.EV_SPE, v => _pk.EV_SPE = v),
        ];
        foreach (var s in Stats)
            s.Changed += Refresh;
        ApplyCommand = new RelayCommand(() => _apply(_pk.Clone()));
        MaxIVsCommand = new RelayCommand(() => { foreach (var s in Stats) s.IV = _pk.MaxIV; });
        Refresh();
    }

    public IReadOnlyList<string> SpeciesList => CoreAdapter.SpeciesNames;
    public IReadOnlyList<string> MoveList => CoreAdapter.MoveNames;
    public IReadOnlyList<string> ItemList => CoreAdapter.ItemNames;
    public IReadOnlyList<string> NatureList => CoreAdapter.NatureNames;
    public IReadOnlyList<StatViewModel> Stats { get; }
    public int MaxIV => _pk.MaxIV;
    public int MaxEV => _pk.MaxEV;

    public RelayCommand ApplyCommand { get; }
    public RelayCommand MaxIVsCommand { get; }

    public string Nickname { get => _pk.Nickname; set { _pk.Nickname = value; Refresh(); } }
    public int Species { get => _pk.Species; set { if (value >= 0) { _pk.Species = (ushort)value; Refresh(); } } }
    public int Level { get => _pk.CurrentLevel; set { _pk.CurrentLevel = (byte)Math.Clamp(value, 1, 100); Refresh(); } }
    public int Nature { get => (int)_pk.Nature; set { if (value >= 0) { _pk.Nature = (Nature)value; Refresh(); } } }
    public int HeldItem { get => _pk.HeldItem; set { if (value >= 0) { _pk.HeldItem = value; Refresh(); } } }
    public int Move1 { get => _pk.Move1; set { if (value >= 0) { _pk.Move1 = (ushort)value; Refresh(); } } }
    public int Move2 { get => _pk.Move2; set { if (value >= 0) { _pk.Move2 = (ushort)value; Refresh(); } } }
    public int Move3 { get => _pk.Move3; set { if (value >= 0) { _pk.Move3 = (ushort)value; Refresh(); } } }
    public int Move4 { get => _pk.Move4; set { if (value >= 0) { _pk.Move4 = (ushort)value; Refresh(); } } }

    public string AbilityName => (uint)_pk.Ability < CoreAdapter.AbilityNames.Count ? CoreAdapter.AbilityNames[_pk.Ability] : "?";
    public bool IsShiny => _pk.IsShiny;
    public Bitmap? Sprite { get; private set; }
    public bool IsLegal { get; private set; }
    public string LegalityText { get; private set; } = "";
    public string LegalityReport { get; private set; } = "";

    private void Refresh()
    {
        Sprite = SpriteService.GetSprite(_pk);
        (IsLegal, LegalityReport) = CoreAdapter.CheckLegality(_pk);
        LegalityText = IsLegal ? "Legal" : "Ilegal";
        foreach (var p in (string[])[nameof(Sprite), nameof(IsLegal), nameof(LegalityText), nameof(LegalityReport), nameof(AbilityName), nameof(IsShiny)])
            Raise(p);
    }
}

public sealed class StatViewModel(string name, Func<int> getIV, Action<int> setIV, Func<int> getEV, Action<int> setEV) : ViewModelBase
{
    public event Action? Changed;
    public string Name { get; } = name;
    public int IV { get => getIV(); set { setIV(value); Raise(); Changed?.Invoke(); } }
    public int EV { get => getEV(); set { setEV(value); Raise(); Changed?.Invoke(); } }
}
