using System;
using System.Collections.Generic;
using System.IO;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>
/// Ponte unica entre a interface e o PKHeX.Core.
/// Se uma atualizacao do upstream mudar alguma API, normalmente basta ajustar este arquivo.
/// </summary>
public static class CoreAdapter
{
    public static IReadOnlyList<string> SpeciesNames => GameInfo.Strings.specieslist;
    public static IReadOnlyList<string> MoveNames => GameInfo.Strings.movelist;
    public static IReadOnlyList<string> ItemNames => GameInfo.Strings.itemlist;
    public static IReadOnlyList<string> NatureNames => GameInfo.Strings.natures;
    public static IReadOnlyList<string> AbilityNames => GameInfo.Strings.abilitylist;

    /// <summary>Codigo de idioma do PKHeX (en, ja, fr, it, de, es, ko, zh-Hans, zh-Hant...).</summary>
    public static void SetLanguage(string code) => GameInfo.CurrentLanguage = code;

    public static SaveFile? LoadSave(string path)
    {
        if (!SaveUtil.TryGetSaveFile(path, out var sav))
            return null;
        OnSaveLoaded(sav);
        return sav;
    }

    private static void OnSaveLoaded(SaveFile sav)
    {
        GameInfo.FilteredSources = new FilteredGameDataSource(sav, GameInfo.Sources);
        Drawing.PokeSprite.SpriteUtil.Initialize(sav);
    }

    public static void ExportSave(SaveFile sav, string path)
    {
        var data = sav.Write();
        File.WriteAllBytes(path, data.Span);
    }

    public static string GetGameName(SaveFile sav) => GameInfo.GetVersionName(sav.Version);

    public static string GetBoxName(SaveFile sav, int box)
        => sav is IBoxDetailNameRead n ? n.GetBoxName(box) : $"Box {box + 1}";

    public static bool IsEmpty(PKM pk) => pk.Species == 0;

    /// <summary>Pokemon em branco ja preenchido com os dados do treinador do save (como no PKHeX original).</summary>
    public static PKM CreateBlank(SaveFile sav)
    {
        var pk = sav.BlankPKM;
        EntityTemplates.TemplateFields(pk, sav);
        return pk;
    }

    public static PKM GetBoxSlot(SaveFile sav, int box, int slot) => sav.GetBoxSlotAtIndex(box, slot);

    public static void SetBoxSlot(SaveFile sav, PKM pk, int box, int slot)
    {
        pk.RefreshChecksum();
        sav.SetBoxSlotAtIndex(pk, box, slot);
    }

    public static int GetPartyCount(SaveFile sav) => sav.HasParty ? 6 : 0;
    public static PKM GetPartySlot(SaveFile sav, int slot) => slot < sav.PartyCount ? sav.GetPartySlotAtIndex(slot) : sav.BlankPKM;

    public static void SetPartySlot(SaveFile sav, PKM pk, int slot)
    {
        pk.RefreshChecksum();
        sav.SetPartySlotAtIndex(pk, Math.Min(slot, sav.PartyCount));
    }

    // Atributos e tipos
    /// <summary>Atributos finais na ordem da UI: PS, Atq, Def, AtE, DeE, Vel.</summary>
    public static int[] GetFinalStats(PKM pk)
    {
        var s = pk.GetStats(pk.PersonalInfo); // H/A/B/S/C/D
        return [s[0], s[1], s[2], s[4], s[5], s[3]];
    }

    /// <summary>Atributos base da especie, mesma ordem da UI.</summary>
    public static int[] GetBaseStats(PKM pk)
    {
        var p = pk.PersonalInfo;
        return [p.HP, p.ATK, p.DEF, p.SPA, p.SPD, p.SPE];
    }

    /// <summary>Modificador da natureza por atributo (UI order): +1, -1 ou 0.</summary>
    public static int[] GetNatureModifiers(PKM pk)
    {
        var result = new int[6];
        var n = (int)pk.StatAlignment;
        if (n >= 25 || n / 5 == n % 5)
            return result;
        // ordem interna da natureza: Atq, Def, Vel, AtE, DeE -> indices na UI
        ReadOnlySpan<int> map = [1, 2, 5, 3, 4];
        result[map[n / 5]] = 1;
        result[map[n % 5]] = -1;
        return result;
    }

    public static IReadOnlyList<(string Name, uint Argb)> GetTypes(PKM pk)
    {
        var p = pk.PersonalInfo;
        var names = GameInfo.Strings.types;
        var list = new List<(string, uint)> { (names[p.Type1], (uint)Drawing.PokeSprite.TypeColor.GetTypeSpriteColor(p.Type1).ToArgb()) };
        if (p.Type2 != p.Type1)
            list.Add((names[p.Type2], (uint)Drawing.PokeSprite.TypeColor.GetTypeSpriteColor(p.Type2).ToArgb()));
        return list;
    }

    public static string GetGenderSymbol(PKM pk) => pk.Gender switch { 0 => "♂", 1 => "♀", _ => "" };

    // Showdown
    public static string ToShowdown(PKM pk) => ShowdownParsing.GetShowdownText(pk);

    /// <summary>Aplica um set Showdown ao Pokemon. Retorna mensagem de erro ou null.</summary>
    public static string? ApplyShowdown(PKM pk, string text)
    {
        if (!ShowdownParsing.TryParseAnyLanguage(text, out var set) || set.Species == 0)
            return "Texto Showdown inválido.";
        pk.ApplySetDetails(set);
        return set.InvalidLines.Count == 0 ? null : $"{set.InvalidLines.Count} linha(s) ignorada(s).";
    }

    // Mochila
    public static PlayerBag GetBag(SaveFile sav) => sav.Inventory;
    public static void SaveBag(SaveFile sav, PlayerBag bag) => bag.CopyTo(sav);
    public static bool IsBagItemIdEditable(SaveFile sav) => sav is not (SAV9ZA or SAV9SV);

    public static (bool Valid, string Report) CheckLegality(PKM pk)
    {
        try
        {
            var la = new LegalityAnalysis(pk);
            return (la.Valid, la.Report());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
