using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

public sealed record DexPlacement(ushort Species, byte Form, DbEntry? Source, PKM? Pokemon, int Box, int Slot, string Note, bool Copy);
public sealed record DexRelocation(int FromBox, int FromSlot, int ToBox, int ToSlot, PKM Pokemon);
public sealed record DexOrigin(DbEntry Entry, byte[] Stored);
public sealed class LivingDexApplicationPlan
{
    public required SaveFile Target { get; init; }
    public required byte[] Before { get; init; }
    public required IReadOnlyDictionary<SlotHistory.Key, PKM> BeforeSlots { get; init; }
    public required IReadOnlyList<PKM> BeforeParty { get; init; }
    public required SaveFile Arrangement { get; init; }
    public required IReadOnlyList<DexPlacement> Placements { get; init; }
    public required IReadOnlyList<DexRelocation> Relocations { get; init; }
    public required IReadOnlyList<DexOrigin> Origins { get; init; }
    public required IReadOnlyList<SlotHistory.Key> Changes { get; init; }
    public required IReadOnlyList<string> Skipped { get; init; }
    public string? Blocked { get; init; }
    public int Missing { get; init; }
    public int BoxesNeeded { get; init; }
    public int Moved => Placements.Count(p => p.Source is not null && !p.Copy && (p.Source.Box != p.Box || p.Source.Slot != p.Slot));
    public int Copied => Placements.Count(p => p.Copy);
    public string Summary => string.Format(Loc.T("{0} movidos, {1} copiados de fora, {2} ocupantes realocados, {3} faltantes"), Moved, Copied, Relocations.Count, Missing);
}

/// <summary>Plans on copies, preserves every unrelated occupant, then applies exactly the preview in one transaction.</summary>
public static class LivingDexApplication
{
    public static (PKM? Pokemon, bool Legal, string? Error) Convert(DbEntry entry, SaveFile target)
    {
        try
        {
            var converted = CoreAdapter.ConvertForSave(target, entry.Pkm.Clone(), out var error);
            if (converted is null) return (null, false, error);
            // Prepare imports on a clone so handler changes are already part of the preview.
            var scratch = target.Clone();
            scratch.SetBoxSlotAtIndex(converted, 0, 0);
            converted = scratch.GetBoxSlotAtIndex(0, 0);
            var la = new LegalityAnalysis(converted, target.Personal, StorageSlotType.Box);
            return (converted, la.Valid, la.Valid ? null : la.Report());
        }
        catch (Exception ex) { return (null, false, ex.Message); }
    }

    public static LivingDexApplicationPlan Prepare(SaveFile target, string path, IReadOnlyList<DbEntry> entries,
        bool forms, bool shiny, bool legalMode, int firstBox, bool reserveMissing,
        IReadOnlyDictionary<DbEntry, (PKM? Pokemon, bool Legal, string? Error)> converted)
    {
        var arrangement = target.Clone();
        var placements = new List<DexPlacement>(); var relocations = new List<DexRelocation>();
        var origins = new List<DexOrigin>(); var changes = new List<SlotHistory.Key>();
        var before = target.Data.ToArray();
        var beforeSlots = Enumerable.Range(0, target.BoxCount).SelectMany(b => Enumerable.Range(0, target.BoxSlotCount).Select(s => new SlotHistory.Key(b, s)))
            .ToDictionary(k => k, k => target.GetBoxSlotAtIndex(k.Box, k.Slot).Clone());
        var beforeParty = target.HasParty ? Enumerable.Range(0, target.PartyCount).Select(target.GetPartySlotAtIndex).ToArray() : [];
        bool Local(DbEntry e) => !e.Source.IsBank && StoredPokemon.SameSource(path, e.Source.Id);
        bool Protected(int b, int s) => target.GetBoxSlotFlags(b, s).IsOverwriteProtected();
        var allowed = entries.Where(e => LivingDexPlanner.Eligible(e, target, shiny) && e.Box >= 0
            && (!Local(e) || !Protected(e.Box, e.Slot))).ToArray();
        var legalities = converted.ToDictionary(p => p.Key, p => p.Value.Legal);
        var template = LivingDexPlanner.Build(entries, target, forms, shiny, legalities);
        var indexed = allowed.GroupBy(e => (e.Pkm.Species, Form: forms ? e.Pkm.Form : (byte)0)).ToDictionary(g => g.Key, g => g
            .OrderByDescending(e => converted.TryGetValue(e, out var c) && c.Legal)
            .ThenByDescending(Local).ThenByDescending(e => e.Pkm.Version == target.Version).ThenByDescending(e => e.Pkm.CurrentLevel)
            .ThenBy(e => e.LocationId, StringComparer.Ordinal).ToArray());
        var choices = new List<(LivingDexRow Row, DbEntry? Entry, PKM? Pk, string Note)>();
        foreach (var row in template.Rows)
        {
            indexed.TryGetValue((row.Species, forms ? row.Form : (byte)0), out var candidates);
            DbEntry? chosen = null; PKM? pokemon = null;
            foreach (var candidate in candidates ?? [])
            {
                if (!converted.TryGetValue(candidate, out var c) || c.Pokemon is null || legalMode && !c.Legal) continue;
                // A persisted attachment to this save must use the existing copy, never duplicate it from Bank.
                if (candidate.Source.IsBank && BankLinks.Find(candidate.Pkm) is { } link && StoredPokemon.SameSource(link.SavePath, path)
                    && entries.Any(e => Local(e) && BankLinks.IdOf(e.Pkm) == link.Id)) continue;
                chosen = candidate; pokemon = c.Pokemon.Clone(); break;
            }
            string note = chosen is not null ? "" : entries.Any(e => Local(e) && e.Box < 0 && e.Pkm.Species == row.Species && (!forms || e.Pkm.Form == row.Form))
                ? Loc.T("Na equipe: tire da equipe para usar.")
                : entries.Any(e => Local(e) && e.Box >= 0 && Protected(e.Box, e.Slot) && e.Pkm.Species == row.Species && (!forms || e.Pkm.Form == row.Form))
                ? Loc.T("Candidato em slot protegido.")
                : candidates?.Length > 0 ? Loc.T("Sem candidato compatível com o modo legal.") + " " + converted.GetValueOrDefault(candidates[0]).Error : Loc.T("Faltando");
            choices.Add((row, chosen, pokemon, note));
        }
        var used = choices.Where(c => c.Entry is not null && Local(c.Entry)).Select(c => new SlotHistory.Key(c.Entry!.Box, c.Entry.Slot)).ToHashSet();
        int missing = choices.Count(c => c.Entry is null);
        var skipped = choices.Where(c => c.Entry is null && c.Note != Loc.T("Faltando"))
            .Select(c => string.Format(Loc.T("Fora do plano: {0} — {1}"), c.Row.Name, c.Note)).ToArray();
        if (!reserveMissing) choices = choices.Where(c => c.Entry is not null).ToList();
        if (firstBox < 0 || firstBox >= target.BoxCount) return Finish(Loc.T("Escolha uma primeira caixa válida."), 0);
        var available = Enumerable.Range(firstBox, target.BoxCount - firstBox).SelectMany(b => Enumerable.Range(0, target.BoxSlotCount)
            .Where(s => !Protected(b, s)).Select(s => new SlotHistory.Key(b, s))).ToArray();
        int need = (choices.Count + target.BoxSlotCount - 1) / target.BoxSlotCount;
        if (choices.Count > available.Length) return Finish(string.Format(Loc.T("Precisa de {0} caixas; este jogo tem {1} a partir da caixa {2}. Slots protegidos não podem ser usados."), need, target.BoxCount - firstBox, firstBox + 1), need);
        int lastBox = choices.Count == 0 ? firstBox - 1 : available[choices.Count - 1].Box;
        int boxesNeeded = lastBox < firstBox ? 0 : lastBox - firstBox + 1;
        var area = Enumerable.Range(firstBox, boxesNeeded).SelectMany(b => Enumerable.Range(0, target.BoxSlotCount)
            .Where(s => !Protected(b, s)).Select(s => new SlotHistory.Key(b, s))).ToArray();
        var occupants = area.Where(k => !used.Contains(k) && target.GetBoxSlotAtIndex(k.Box, k.Slot).Species > 0).ToArray();
        var freeOutside = Enumerable.Range(0, target.BoxCount).Where(b => b < firstBox || b > lastBox)
            .SelectMany(b => Enumerable.Range(0, target.BoxSlotCount).Select(s => new SlotHistory.Key(b, s)))
            .Where(k => !Protected(k.Box, k.Slot) && (target.GetBoxSlotAtIndex(k.Box, k.Slot).Species == 0 || used.Contains(k))).ToArray();
        if (occupants.Length > freeOutside.Length) return Finish(Loc.T("Não há espaço fora da área para preservar todos os ocupantes."), boxesNeeded);
        foreach (var k in used.Concat(area)) arrangement.SetBoxSlotAtIndex(target.BlankPKM, k.Box, k.Slot, EntityImportSettings.None);
        for (int i = 0; i < occupants.Length; i++)
        {
            var src = occupants[i]; var dst = freeOutside[i]; var pk = target.GetBoxSlotAtIndex(src.Box, src.Slot).Clone();
            arrangement.SetBoxSlotAtIndex(pk, dst.Box, dst.Slot, EntityImportSettings.None);
            relocations.Add(new(src.Box, src.Slot, dst.Box, dst.Slot, pk));
        }
        for (int i = 0; i < choices.Count; i++)
        {
            var choice = choices[i]; var dst = available[i]; bool copy = choice.Entry is not null && !Local(choice.Entry);
            if (choice.Pk is not null) arrangement.SetBoxSlotAtIndex(choice.Pk.Clone(), dst.Box, dst.Slot, EntityImportSettings.None);
            var actual = choice.Pk is null ? null : arrangement.GetBoxSlotAtIndex(dst.Box, dst.Slot);
            placements.Add(new(choice.Row.Species, choice.Row.Form, choice.Entry, actual, dst.Box, dst.Slot, choice.Note, copy));
            if (choice.Entry is not null) origins.Add(new(choice.Entry, StoredPokemon.Bytes(choice.Entry.Pkm)));
        }
        for (int b = 0; b < target.BoxCount; b++)
            for (int s = 0; s < target.BoxSlotCount; s++)
                if (!StoredPokemon.Equal(target.GetBoxSlotAtIndex(b, s), arrangement.GetBoxSlotAtIndex(b, s))) changes.Add(new(b, s));
        return Finish(null, boxesNeeded);
        LivingDexApplicationPlan Finish(string? error, int boxes) => new()
        {
            Target = target, Before = before, BeforeSlots = beforeSlots, BeforeParty = beforeParty, Arrangement = arrangement, Placements = placements, Relocations = relocations,
            Origins = origins, Changes = changes, Skipped = skipped, Blocked = error, Missing = missing, BoxesNeeded = boxes,
        };
    }

    public static bool Validate(LivingDexApplicationPlan plan, SaveFile current, Func<DbEntry, PKM?> readOrigin)
    {
        if (plan.Blocked is not null || !ReferenceEquals(plan.Target, current)) return false;
        if (plan.BeforeSlots.Any(p => !StoredPokemon.Equal(p.Value, current.GetBoxSlotAtIndex(p.Key.Box, p.Key.Slot)))) return false;
        if (current.HasParty && (current.PartyCount != plan.BeforeParty.Count || plan.BeforeParty.Where((pk, i) => !StoredPokemon.Equal(pk, current.GetPartySlotAtIndex(i))).Any())) return false;
        foreach (var origin in plan.Origins)
        {
            var pk = readOrigin(origin.Entry);
            if (pk is null || pk.GetType() != origin.Entry.Pkm.GetType() || !origin.Stored.SequenceEqual(pk.Data[..pk.SIZE_STORED].ToArray())) return false;
        }
        return plan.Changes.All(k => !current.GetBoxSlotFlags(k.Box, k.Slot).IsOverwriteProtected());
    }

    public static void Apply(LivingDexApplicationPlan plan, SaveFile current, SlotHistory history, Func<DbEntry, PKM?> readOrigin, Action<int>? afterWrite = null)
    {
        if (!Validate(plan, current, readOrigin)) throw new InvalidOperationException(Loc.T("A origem ou o destino mudou. Refazer a prévia é necessário."));
        if (plan.Changes.Count == 0) return;
        history.Record(Loc.T("aplicar Living Dex"), plan.Changes.ToArray());
        try
        {
            int n = 0;
            foreach (var key in plan.Changes)
            {
                current.SetBoxSlotAtIndex(plan.Arrangement.GetBoxSlotAtIndex(key.Box, key.Slot).Clone(), key.Box, key.Slot, EntityImportSettings.None);
                afterWrite?.Invoke(++n);
            }
        }
        catch { history.Rollback(); throw; }
    }
}
