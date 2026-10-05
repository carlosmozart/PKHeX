using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

public sealed record PokemonChange(string Field, string Before, string After, bool Important)
{
    public string Text => $"{(Important ? "⚠ " : "")}{Loc.T(Field)}: {Value(Before)} → {Value(After)}";
    private string Value(string value) => Field is "OT" or "Apelido" ? value
        : Field is "Shiny" or "Legalidade" || value == "Desconhecido" ? Loc.T(value) : value;
}

/// <summary>Read-only comparison of the actual candidate, shared by legalization and transfers.</summary>
public static class PokemonDiff
{
    public static IReadOnlyList<PokemonChange> Compare(PKM before, PKM after)
    {
        var a = Values(before); var b = Values(after);
        return a.Where(x => x.Value.Value != b[x.Key].Value || RawChanged(before, after, x.Key))
            .Select(x => new PokemonChange(x.Key, x.Value.Value, b[x.Key].Value, x.Value.Important)).ToArray();
    }

    // Unknown IDs can share a display label; they still represent a change.
    private static bool RawChanged(PKM a, PKM b, string field) => field switch
    {
        "Espécie/forma" => a.Species != b.Species || a.Form != b.Form,
        "Habilidade" => a.Ability != b.Ability,
        "Item" => a.HeldItem != b.HeldItem,
        "Bola" => a.Ball != b.Ball,
        "Idioma" => a.Language != b.Language,
        "Local do encontro" => a.MetLocation != b.MetLocation,
        "Golpes" => !a.Moves.SequenceEqual(b.Moves),
        _ => false,
    };

    public static IReadOnlyList<string> Details(PKM before, PKM after)
    {
        var changes = Compare(before, after);
        return [Summary(changes), .. changes.Select(c => c.Text)];
    }

    public static string Summary(IReadOnlyList<PokemonChange> changes) => changes.Count == 0
        ? Loc.T("Nenhuma mudança nos campos comparados.")
        : changes.Any(c => c.Important) ? Loc.T("Confira as mudanças importantes marcadas com ⚠.")
        : Loc.T("Mudam apenas campos de importância normal: ") + string.Join(", ", changes.Select(c => Loc.T(c.Field)));

    public static IReadOnlyList<string> Batch(IEnumerable<(PKM Before, PKM After)> pairs)
    {
        var items = pairs.Select(p => (p.Before, Changes: Compare(p.Before, p.After))).ToArray();
        return [.. items.SelectMany(x => x.Changes).GroupBy(c => (c.Field, c.Before, c.After))
            .Select(g => $"{g.Count()} Pokémon · {g.First().Text}"),
            .. items.SelectMany(x => new[] { "— " + CoreAdapter.SpeciesNames[x.Before.Species] }.Concat(x.Changes.Select(c => c.Text)))];
    }

    public static string BatchSummary(IEnumerable<(PKM Before, PKM After)> pairs)
    {
        var all = pairs.ToArray();
        var fields = all.SelectMany(p => Compare(p.Before, p.After)).GroupBy(c => c.Field)
            .Select(g => $"{g.Count()} · {Loc.T(g.Key)}");
        return $"{all.Length} Pokémon: " + string.Join("; ", fields);
    }

    private static Dictionary<string, (string Value, bool Important)> Values(PKM pk)
    {
        var s = GameInfo.Strings;
        string Name(IReadOnlyList<string> names, int index) => (uint)index < (uint)names.Count ? names[index] : "Desconhecido";
        string Form()
        {
            var forms = FormConverter.GetFormList(pk.Species, s.types, s.forms, GameInfo.GenderSymbolUnicode, pk.Context);
            return Name(s.specieslist, pk.Species) + (forms.Length > 1 ? " · " + Name(forms, pk.Form) : "");
        }
        string Stats(bool iv) => string.Join(" / ", iv
            ? new[] { pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE }
            : new[] { pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE });
        var ribbons = CoreAdapter.GetRibbons(pk);
        return new()
        {
            ["Espécie/forma"] = (Form(), false),
            ["Nível"] = (pk.CurrentLevel.ToString(), false),
            ["Natureza"] = (pk.Format >= 3 ? Name(s.natures, (int)pk.Nature) : "—", true),
            ["Menta"] = (pk.Format >= 8 ? Name(s.natures, (int)pk.StatAlignment) : "—", true),
            ["Habilidade"] = (pk.Format >= 3 ? Name(s.abilitylist, pk.Ability) : "—", false),
            ["PID"] = (pk.Format >= 3 ? pk.PID.ToString("X8") : "—", true),
            ["Shiny"] = (pk.IsShiny ? "Sim" : "Não", true),
            ["IVs"] = (Stats(true), true), ["EVs"] = (Stats(false), false),
            ["Golpes"] = (string.Join(" / ", pk.Moves.Select(m => Name(s.movelist, m))), true),
            ["Item"] = (pk.HeldItem == 0 ? "—" : CoreAdapter.GetHeldItemName(pk), false),
            ["Bola"] = (Name(s.balllist, pk.Ball), true),
            ["OT"] = (pk.OriginalTrainerName, true),
            ["TID"] = (pk.TID16.ToString(), true), ["SID"] = (pk.SID16.ToString(), true),
            ["Idioma"] = (Enum.IsDefined((LanguageID)pk.Language) ? ((LanguageID)pk.Language).ToString() : "Desconhecido", false),
            ["Apelido"] = (pk.Nickname, true),
            ["Local do encontro"] = (CoreAdapter.GetMetLocations(pk).FirstOrDefault(x => x.Value == pk.MetLocation)?.Text ?? "Desconhecido", false),
            ["Nível do encontro"] = (pk.MetLevel.ToString(), false),
            ["Data do encontro"] = (pk.MetDate?.ToString("yyyy-MM-dd") ?? "—", false),
            ["Jogo de origem"] = (CoreAdapter.GetVersionName(pk.Version), false),
            ["Fitas e marcas"] = (ribbons.Sum(r => r.Value).ToString(), false),
            ["Pokérus"] = ($"{pk.PokerusStrain} / {pk.PokerusDays}", false),
            ["Hidden Power"] = (pk.Format is >= 2 and <= 7 || pk is PB8 ? CoreAdapter.GetHiddenPowerType(pk).Name : "—", false),
            ["Legalidade"] = (CoreAdapter.IsLegal(pk) == true ? "Legal" : "Ilegal", false),
        };
    }
}
