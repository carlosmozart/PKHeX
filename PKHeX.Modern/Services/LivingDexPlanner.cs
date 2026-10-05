using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

public sealed record LivingDexRow(ushort Species, byte Form, string Name, DbEntry? Candidate,
    bool CandidateLegal, IReadOnlyList<DbEntry> Duplicates, int Box, int Slot)
{
    public bool Missing => Candidate is null;
}

public sealed record LivingDexPlan(IReadOnlyList<LivingDexRow> Rows)
{
    public int Owned => Rows.Count(r => !r.Missing);
    public int Missing => Rows.Count - Owned;
    public int Duplicates => Rows.Sum(r => r.Duplicates.Count);
}

/// <summary>Read-only national-order plan. Candidates are indexed once instead of rescanning for every species.</summary>
public static class LivingDexPlanner
{
    public static bool Eligible(DbEntry entry, SaveFile target, bool shinyOnly)
    {
        var pk = entry.Pkm;
        return !pk.IsEgg && (!shinyOnly || pk.IsShiny) && pk.Species <= target.MaxSpeciesID
            && target.Personal.IsPresentInGame(pk.Species, pk.Form)
            && !FormInfo.IsBattleOnlyForm(pk.Species, pk.Form, target.Generation)
            && !FormInfo.IsTotemForm(pk.Species, pk.Form)
            && !FormInfo.IsLordForm(pk.Species, pk.Form, target.Context);
    }

    /// <summary>
    /// Must run in one UI callback, without awaiting: Core parse context is global. The caller yields between
    /// small chunks and provides the current save so it can be restored before any other UI callback runs.
    /// </summary>
    public static bool AssessLegality(DbEntry entry, SaveFile? activeSave)
    {
        var gb = ParseSettings.AllowGBEraEvents;
        var gba = ParseSettings.AllowGBACrossTransferRSE(entry.Pkm);
        var switchGba = ParseSettings.AllowGen3EventTicketsAll(entry.Pkm);
        try
        {
            if (entry.Source.Save is { } sav) ParseSettings.InitFromSaveFileData(sav);
            else
            {
                ParseSettings.ClearActiveTrainer();
                ParseSettings.AllowEraCartGB = entry.Pkm.Format <= 2;
            }
            var origin = entry.Source.IsBank ? StorageSlotType.None : entry.Box < 0 ? StorageSlotType.Party : StorageSlotType.Box;
            return entry.Source.Save is { } source ? new LegalityAnalysis(entry.Pkm, source.Personal, origin).Valid
                : new LegalityAnalysis(entry.Pkm).Valid;
        }
        catch { return false; }
        finally
        {
            if (activeSave is not null) ParseSettings.InitFromSaveFileData(activeSave); else ParseSettings.ClearActiveTrainer();
            ParseSettings.AllowEraCartGB = gb; ParseSettings.AllowEraCartGBA = gba; ParseSettings.AllowEraSwitchGBA = switchGba;
        }
    }

    public static LivingDexPlan Build(IReadOnlyList<DbEntry> entries, SaveFile target, bool includeForms, bool shinyOnly,
        IReadOnlyDictionary<DbEntry, bool> legalities)
    {
        var indexed = entries.Where(e => Eligible(e, target, shinyOnly)).GroupBy(e => (e.Pkm.Species, Form: includeForms ? e.Pkm.Form : (byte)0))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => legalities.TryGetValue(e, out var legal) && legal)
                .ThenByDescending(e => e.Pkm.Version == target.Version).ThenByDescending(e => e.Pkm.CurrentLevel)
                .ThenBy(e => e.Source.Id, StringComparer.Ordinal).ThenBy(e => e.Box).ThenBy(e => e.Slot).ToArray());
        var rows = new List<LivingDexRow>();
        for (ushort sp = 1; sp <= target.MaxSpeciesID; sp++)
        {
            if (!target.Personal.IsSpeciesInGame(sp)) continue;
            int forms = includeForms ? Math.Max(1, (int)target.Personal.GetFormEntry(sp, 0).FormCount) : 1;
            for (byte form = 0; form < forms; form++)
            {
                if (includeForms && (!target.Personal.IsPresentInGame(sp, form) || FormInfo.IsBattleOnlyForm(sp, form, target.Generation)
                    || FormInfo.IsTotemForm(sp, form) || FormInfo.IsLordForm(sp, form, target.Context))) continue;
                indexed.TryGetValue((sp, form), out var candidates);
                var best = candidates?.FirstOrDefault();
                var name = CoreAdapter.SpeciesNames[sp];
                if (includeForms)
                {
                    var label = FormConverter.GetStringFromForm(sp, form, GameInfo.Strings, target.Context);
                    if (!string.IsNullOrWhiteSpace(label)) name += " · " + label;
                }
                rows.Add(new(sp, form, name, best, best is not null && legalities.TryGetValue(best, out var legal) && legal,
                    candidates?.Skip(1).ToArray() ?? [], rows.Count / 30 + 1, rows.Count % 30 + 1));
            }
        }
        return new(rows);
    }
}
