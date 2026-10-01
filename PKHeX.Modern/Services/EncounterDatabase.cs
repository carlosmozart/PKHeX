using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using PKHeX.Core;
using PKHeX.Core.Searching;

namespace PKHeX.Modern.Services;

/// <summary>
/// Bancos de encontros e de Mystery Gift, com as mesmas funcoes do PKHeX original
/// (SAV_Encounters e SAV_MysteryGiftDB). Tudo aqui pode rodar fora da thread da interface.
/// </summary>
public static class EncounterDatabase
{
    /// <summary>
    /// Encontros possiveis de uma especie (todas as formas). <paramref name="onlyThisGame"/> limita as versoes
    /// as compativeis com o save, como o "filter unavailable species" do PKHeX.
    /// </summary>
    public static IReadOnlyList<IEncounterInfo> SearchEncounters(SaveFile sav, ushort species, bool onlyThisGame, CancellationToken token = default)
    {
        var settings = new SearchSettings { Context = sav.Context, Generation = sav.Generation, Species = species };
        var versions = onlyThisGame ? settings.GetVersions(sav) : GameUtil.GameVersions;
        var pk = sav.BlankPKM;

        var pi = sav.Personal.GetFormEntry(species, 0);
        var forms = pi.FormCount;
        if (forms == 0 && !onlyThisGame) // especie ausente no jogo: usa a tabela de outra geracao
            forms = PersonalTable.USUM.GetFormEntry(species, 0).FormCount;

        var result = new List<IEncounterInfo>();
        var seen = new HashSet<IEncounterInfo>(ReferenceEqualityComparer.Instance);
        try
        {
            for (byte form = 0; form < Math.Max((byte)1, forms); form++)
            {
                if (FormInfo.IsBattleOnlyForm(species, form, pk.Format))
                    continue;
                pk.Species = species;
                pk.Form = form;
                pk.SetGender(pk.GetSaneGender());
                EncounterMovesetGenerator.OptimizeCriteria(pk, sav);
                foreach (var enc in EncounterMovesetGenerator.GenerateEncounters(pk, ReadOnlyMemory<ushort>.Empty, versions))
                {
                    token.ThrowIfCancellationRequested();
                    if (seen.Add(enc))
                        result.Add(enc);
                }
            }
        }
        finally
        {
            EncounterMovesetGenerator.ResetFilters();
        }

        if (onlyThisGame && EntityPresenceFilters.GetFilterGeneric<IEncounterInfo>(sav.Context) is { } filter)
            return [.. result.Where(filter)];
        return result;
    }

    /// <summary>Todos os Mystery Gifts conhecidos; <paramref name="onlyThisGame"/> mantem so os que existem neste jogo.</summary>
    public static IReadOnlyList<MysteryGift> LoadGifts(SaveFile sav, bool onlyThisGame)
    {
        IEnumerable<MysteryGift> db = EncounterEvent.GetAllEvents();
        if (onlyThisGame && EntityPresenceFilters.GetFilterGift<MysteryGift>(sav.Context, sav.Generation) is { } filter)
            db = db.Where(filter);
        var list = db.ToList();
        foreach (var mg in list)
            mg.GiftUsed = false;
        return list;
    }

    /// <summary>Gera o Pokemon do encontro/gift ja no formato do save. Retorna null e o erro se nao der para converter.</summary>
    public static PKM? ToEntity(SaveFile sav, IEncounterInfo enc, out string? error)
    {
        error = null;
        try
        {
            var temp = enc.ConvertToPKM(sav, EncounterCriteria.Unrestricted);
            var pk = EntityConverter.ConvertToType(temp, sav.PKMType, out var result);
            if (pk is null)
            {
                error = result.GetDisplayString(temp, sav.PKMType);
                return null;
            }
            sav.AdaptToSaveFile(pk);
            pk.RefreshChecksum();
            return pk;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    // Textos para os cartoes
    public static string GetLocationName(IEncounterTemplate enc)
    {
        try { return GameInfo.GetLocationName(enc.IsEgg, enc.Location, enc.Generation, enc.Generation, enc.Version); }
        catch { return ""; }
    }

    public static string GetVersionName(GameVersion version) => GameInfo.GetVersionName(version);

    public static string GetSpeciesFormName(ushort species, byte form, EntityContext context)
    {
        var name = species < CoreAdapter.SpeciesNames.Count ? CoreAdapter.SpeciesNames[species] : $"#{species}";
        if (form == 0)
            return name;
        var formName = FormConverter.GetStringFromForm(species, form, GameInfo.Strings, context);
        return string.IsNullOrWhiteSpace(formName) ? name : $"{name} ({formName})";
    }

    /// <summary>Resumo de varias linhas (o mesmo do tooltip do PKHeX original).</summary>
    public static string GetDetails(IEncounterInfo enc)
    {
        try { return string.Join(Environment.NewLine, enc.GetTextLines()); }
        catch { return ""; }
    }
}
