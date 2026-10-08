using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    public bool HasPokeathlon => _sav is SAV4HGSS;
    public bool IsPokeathlonTab => Tab == 13;
    public PokeathlonMedalsViewModel? Pokeathlon { get; private set; }
    private void RefreshPokeathlon()
    {
        Pokeathlon = _sav is SAV4HGSS h ? new(h, Edit) : null;
        Raise(nameof(Pokeathlon)); Raise(nameof(HasPokeathlon));
    }
}

/// <summary>Medalhas do Pokéathlon por espécie (HeartGold/SoulSilver): uma por percurso (Speed, Power, Skill, Stamina, Jump).</summary>
public sealed class PokeathlonMedalsViewModel : ViewModelBase
{
    private readonly SAV4HGSS _sav;
    private readonly Action<string, Action> _edit;
    private readonly List<PokeathlonSpeciesViewModel> _all;

    public PokeathlonMedalsViewModel(SAV4HGSS sav, Action<string, Action> edit)
    {
        _sav = sav; _edit = edit;
        var names = GameInfo.Strings.specieslist;
        _all = [.. Enumerable.Range(1, PokeathlonMedalManager4.SIZE).Select(s => new PokeathlonSpeciesViewModel((ushort)s, names[s], this))];
        ApplyFilter();
    }

    public static IReadOnlyList<string> Courses { get; } = ["Speed", "Power", "Skill", "Stamina", "Jump"];
    public IReadOnlyList<PokeathlonSpeciesViewModel> Rows { get; private set; } = [];
    public string Summary => $"{Medals.GetTotalCount()} de {PokeathlonMedalManager4.SIZE * 5} medalhas · {_all.Count(r => r.Bits == PokeathlonMedalManager4.MaxMedalBits)} espécies com as 5";

    private string _query = "";
    public string Query { get => _query; set { if (Set(ref _query, value ?? "")) ApplyFilter(); } }
    private bool _onlyMissing;
    public bool OnlyMissing { get => _onlyMissing; set { if (Set(ref _onlyMissing, value)) ApplyFilter(); } }

    private void ApplyFilter()
    {
        var q = _query.Trim();
        Rows = [.. _all.Where(r => (q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Number.Contains(q))
            && (!_onlyMissing || r.Bits != PokeathlonMedalManager4.MaxMedalBits))];
        Raise(nameof(Rows));
    }

    public RelayCommand AllCommand => new(() => SetAll(PokeathlonMedalManager4.MaxMedalBits, "Todas as medalhas do Pokéathlon"));
    public RelayCommand ClearCommand => new(() => SetAll(0, "Tirar as medalhas do Pokéathlon"));
    private void SetAll(byte bits, string description)
    {
        _edit(description, () => { var m = Medals; m.SetAllMedals(bits); });
        foreach (var r in _all) r.Refresh();
        Raise(nameof(Summary));
        if (_onlyMissing) ApplyFilter();
    }

    private PokeathlonMedalManager4 Medals => _sav.Pokeathlon.Medals;
    internal byte Get(ushort species) => Medals.GetMedal(species);
    internal void Set(ushort species, byte bits)
    {
        _edit($"Medalhas do Pokéathlon: {GameInfo.Strings.specieslist[species]}", () => { var m = Medals; m.SetMedal(species, bits); });
        Raise(nameof(Summary));
    }
}

public sealed class PokeathlonSpeciesViewModel(ushort species, string name, PokeathlonMedalsViewModel owner) : ViewModelBase
{
    public string Name => name;
    public string Number => $"#{species:000}";
    private Bitmap? _sprite; private bool _loaded;
    public Bitmap? Sprite { get { if (!_loaded) { _loaded = true; _sprite = SpriteService.GetSpeciesSprite(species, false); } return _sprite; } }
    internal byte Bits => owner.Get(species);
    public bool Speed { get => Has(0); set => Toggle(0, value); }
    public bool Power { get => Has(1); set => Toggle(1, value); }
    public bool Skill { get => Has(2); set => Toggle(2, value); }
    public bool Stamina { get => Has(3); set => Toggle(3, value); }
    public bool Jump { get => Has(4); set => Toggle(4, value); }
    public bool All { get => Bits == PokeathlonMedalManager4.MaxMedalBits; set { owner.Set(species, value ? PokeathlonMedalManager4.MaxMedalBits : (byte)0); Refresh(); } }
    private bool Has(int bit) => (Bits >> bit & 1) != 0;
    private void Toggle(int bit, bool value)
    {
        if (value == Has(bit)) return;
        owner.Set(species, (byte)(value ? Bits | (1 << bit) : Bits & ~(1 << bit)));
        Refresh();
    }
    internal void Refresh() { foreach (var p in (string[])[nameof(Speed), nameof(Power), nameof(Skill), nameof(Stamina), nameof(Jump), nameof(All)]) Raise(p); }
}
