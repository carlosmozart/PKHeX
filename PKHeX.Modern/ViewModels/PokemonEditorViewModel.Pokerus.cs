using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.Modern.ViewModels;

public sealed partial class PokemonEditorViewModel
{
    private bool _pokerusObtainable;
    public bool HasPokerus => _pk.Format >= 2 && _pk is not (PB7 or PK9 or PA9);
    public bool CanEditPokerus => HasPokerus && (!LegalMode || _pokerusObtainable);
    public string PokerusNote => LegalMode && !_pokerusObtainable
        ? "Modo legal: este Pokémon não pode ter Pokérus neste jogo."
        : "O Pokérus dobra os EVs ganhos em batalha, mesmo após a cura.";
    public IReadOnlyList<string> PokerusStates { get; } = ["Sem Pokérus", "Infectado", "Curado"];
    public int PokerusState
    {
        get => _pk.IsPokerusInfected ? 1 : _pk.IsPokerusCured ? 2 : 0;
        set
        {
            if (value is < 0 or > 2 || value == PokerusState) return;
            int strain = Math.Clamp(_pk.PokerusStrain, 1, MaxPokerusStrain);
            SetPokerus(value == 0 ? 0 : strain, value == 1 ? Pokerus.GetMaxDuration(strain) : 0);
        }
    }
    public int MaxPokerusStrain => _pk.Format == 2 ? 8 : 15;
    public int MaxPokerusDays => Pokerus.GetMaxDuration(_pk.PokerusStrain);
    public int PokerusStrain
    {
        get => _pk.PokerusStrain;
        set => SetPokerus(Math.Clamp(value, 0, MaxPokerusStrain),
            Math.Min(_pk.PokerusDays, Pokerus.GetMaxDuration(Math.Clamp(value, 0, MaxPokerusStrain))));
    }
    public int PokerusDays
    {
        get => _pk.PokerusDays;
        set => SetPokerus(_pk.PokerusStrain, Math.Clamp(value, 0, MaxPokerusDays));
    }
    public RelayCommand GivePokerusCommand { get; }

    private void SetPokerus(int strain, int days)
    {
        if (!CanEditPokerus) { _status(PokerusNote); return; }
        var encounter = new LegalityAnalysis(_pk).EncounterMatch;
        if (!Pokerus.IsStrainValid(_pk, encounter, strain, days) || !Pokerus.IsDurationValid(strain, days, out _))
            return;
        if (strain == _pk.PokerusStrain && days == _pk.PokerusDays) return;
        _pk.PokerusStrain = strain;
        _pk.PokerusDays = days;
        Refresh();
    }

    private void RefreshPokerus()
    {
        _pokerusObtainable = HasPokerus && Pokerus.IsObtainable(_pk, new LegalityAnalysis(_pk).EncounterMatch);
        foreach (var name in (string[])[nameof(HasPokerus), nameof(CanEditPokerus), nameof(PokerusState),
            nameof(PokerusStrain), nameof(PokerusDays), nameof(MaxPokerusStrain), nameof(MaxPokerusDays), nameof(PokerusNote)])
            Raise(name);
        GivePokerusCommand.NotifyCanExecuteChanged();
    }
}
