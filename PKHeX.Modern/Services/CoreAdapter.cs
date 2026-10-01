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

    public static PKM GetBoxSlot(SaveFile sav, int box, int slot) => sav.GetBoxSlotAtIndex(box, slot);

    public static void SetBoxSlot(SaveFile sav, PKM pk, int box, int slot)
    {
        pk.RefreshChecksum();
        sav.SetBoxSlotAtIndex(pk, box, slot);
    }

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
