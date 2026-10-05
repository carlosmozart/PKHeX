using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Um Pokemon do save aberto que entra no lote. <see cref="Box"/> &lt; 0 = equipe.</summary>
public sealed record BatchTarget(int Box, int Slot, PKM Pkm, string Where);

/// <summary>Resultado de um Pokemon: como ficaria e se continua legal.</summary>
public sealed record BatchChange(BatchTarget Target, PKM Result, bool? WasLegal, bool? IsLegal)
{
    /// <summary>Era legal (ou nao foi possivel analisar) e ficaria ilegal.</summary>
    public bool BecomesIllegal => WasLegal != false && IsLegal == false;
}

/// <summary>Acao rapida da edicao em lote: codigo proprio ou uma linha do script do PKHeX.</summary>
public sealed record BatchAction(string Name, string Tip, Func<PKM, bool>? Apply = null, string? Script = null, bool Legalize = false);

/// <summary>Resumo de uma execucao (pre-visualizacao ou aplicacao).</summary>
public sealed record BatchRun(int Checked, int Matched, IReadOnlyList<BatchChange> Changes, int Errors, int LegalizeFailed = 0, int Legalized = 0);

/// <summary>
/// Edicao em lote sobre o save aberto. Usa o mesmo script do Batch Editor do PKHeX
/// (<c>=Species=Pikachu</c> filtra, <c>.CurrentLevel=100</c> altera) mais acoes rapidas.
/// Trabalha em copias: quem grava no save e o <see cref="ViewModels.MainViewModel"/>.
/// </summary>
public static class BatchEditor
{
    public static IReadOnlyList<BatchAction> Actions { get; } =
    [
        new("Nível 100", "Sobe para o nível 100 (atributos recalculados).", pk =>
        {
            if (pk.CurrentLevel >= 100)
                return false;
            pk.CurrentLevel = 100;
            return true;
        }),
        new("IVs máximos", "Todos os IVs no máximo. Na Gen 1/2, um shiny fica com os maiores DVs que mantêm o shiny.", SetMaxIVs),
        new("Zerar EVs", "Todos os EVs em 0.", pk =>
        {
            pk.SetEVs([0, 0, 0, 0, 0, 0]);
            return true;
        }),
        new("Felicidade máxima", "Felicidade 255 (ovos ficam como estão).", pk =>
        {
            if (pk.IsEgg)
                return false;
            pk.CurrentFriendship = 255;
            return true;
        }),
        new("Curar PS e PP", "PS cheio, sem status e PP recuperados.", pk =>
        {
            pk.Heal();
            return true;
        }),
        new("Tornar shiny", "Shiny pelo PID (os que têm shiny bloqueado no jogo ficam de fora).", pk =>
        {
            if (pk.IsShiny || pk.IsEgg || CoreAdapter.IsShinyLocked(pk))
                return false;
            pk.SetShiny();
            return true;
        }),
        new("Golpes sugeridos", "Troca os golpes por um conjunto legal sugerido pelo PKHeX.", Script: ".Moves=$suggest"),
        new("Pokébola legal", "Troca a Pokébola por uma legal, combinando com a cor do Pokémon.", Script: ".Ball=$suggest"),
        new("Legalizar", "Gera de novo, a partir de um encontro real do jogo, os que estiverem ilegais (depois das outras ações), mantendo natureza, nível, item, apelido e golpes quando possível. Pode levar alguns segundos por Pokémon.", Legalize: true),
    ];

    private static bool SetMaxIVs(PKM pk)
    {
        int m = pk.MaxIV;
        if (pk.Format <= 2 && pk.IsShiny)
            pk.SetIVs([m, m, 10, 10, 10, 10]); // DVs shiny: Atq qualquer valor com bit 2, Def/Vel/Esp 10
        else
            pk.SetIVs([m, m, m, m, m, m]);
        return true;
    }

    /// <summary>Le o script. Linhas vazias separam conjuntos; <c>;</c> sozinho numa linha tambem.</summary>
    public static bool TryParse(string? text, out StringInstructionSet[] sets, out string? error)
    {
        sets = [];
        error = null;
        var lines = (text ?? "").Split('\n', StringSplitOptions.TrimEntries).Where(l => l.Length > 0 && !l.StartsWith("//")).ToArray();
        if (lines.Length == 0)
            return true;
        var bad = lines.FirstOrDefault(l => l != ";" && !StringInstruction.TryParseFilter(l, out _) && !StringInstruction.TryParseInstruction(l, out _));
        if (bad is not null)
        {
            error = $"Linha inválida: “{bad}”. Use =Propriedade=valor para filtrar e .Propriedade=valor para alterar.";
            return false;
        }
        try
        {
            sets = StringInstructionSet.GetBatchSets(lines);
        }
        catch (Exception ex)
        {
            error = $"Script inválido: {ex.Message}";
            return false;
        }
        var empty = sets.SelectMany(s => s.Filters.Concat(s.Instructions)).FirstOrDefault(i => string.IsNullOrWhiteSpace(i.PropertyValue));
        if (empty is not null)
        {
            error = $"Falta o valor em “{empty.PropertyName}”.";
            return false;
        }
        var editor = EntityBatchEditor.Instance;
        var unknown = sets.SelectMany(s => s.Filters.Concat(s.Instructions))
            .Select(i => i.PropertyName)
            .FirstOrDefault(p => !IsMeta(p) && !IsCustom(p) && !editor.Properties.Any(list => list.Contains(p)));
        if (unknown is not null)
        {
            error = $"Propriedade desconhecida: “{unknown}”.";
            return false;
        }
        foreach (var set in sets)
        {
            EntityBatchEditor.ScreenStrings(set.Filters);
            EntityBatchEditor.ScreenStrings(set.Instructions);
        }
        return true;
    }

    private static bool IsMeta(string property) => BatchFilters.FilterMeta.Any(z => z.IsMatch(property));
    private static bool IsCustom(string property) => property is "Legal" or "HasType" or "PersonalType1" or "PersonalType2" or "Ribbons" or "EVs"
        or "ContestStats" or "MoveMastery" or "PlusMoves" or "Moves" or "RelearnMoves" or "Stats" or "Heal" or "HealPP" or "IdentifierContains";

    /// <summary>Calcula o lote sem gravar nada. Pokemon que nao mudam ficam fora de <see cref="BatchRun.Changes"/>.</summary>
    public static BatchRun Run(SaveFile sav, IEnumerable<BatchTarget> targets, IReadOnlyList<StringInstructionSet> sets,
        IReadOnlyList<BatchAction> actions, bool includeEggs, Action<int, int>? progress = null)
    {
        bool legalize = actions.Any(a => a.Legalize);
        var list = targets as IReadOnlyCollection<BatchTarget> ?? targets.ToList();
        int done = 0, legalizeFailed = 0, legalized = 0;
        var editor = EntityBatchEditor.Instance;
        var actionScript = actions.Where(a => a.Script is not null)
            .Select(a => StringInstruction.TryParseInstruction(a.Script, out var i) ? i : null).OfType<StringInstruction>().ToList();
        EntityBatchEditor.ScreenStrings(actionScript);

        int checkedCount = 0, matched = 0, errors = 0;
        var changes = new List<BatchChange>();
        foreach (var t in list)
        {
            progress?.Invoke(++done, list.Count);
            var original = t.Pkm;
            if (original.Species == 0 || original.Species > sav.MaxSpeciesID || !original.Valid)
                continue;
            if (original.IsEgg && !includeEggs)
                continue;
            if (t.Box >= 0 && !CoreAdapter.CanWriteBoxSlot(sav, t.Box, t.Slot))
                continue;
            checkedCount++;

            var pk = original.Clone();
            var slot = new SlotCache(t.Box < 0 ? new SlotInfoParty(t.Slot) : new SlotInfoBox(t.Box, t.Slot, sav), pk, sav);
            bool match = sets.Count == 0;
            foreach (var set in sets)
            {
                var meta = set.Filters.Where(f => IsMeta(f.PropertyName)).ToList();
                var filters = set.Filters.Where(f => !IsMeta(f.PropertyName)).ToList();
                if (!EntityBatchEditor.IsFilterMatchMeta(meta, slot) || !editor.IsFilterMatch(filters, pk))
                    continue;
                match = true;
                if (set.Instructions.Count > 0 && editor.TryModify(pk, [], set.Instructions).HasFlag(ModifyResult.Error))
                    errors++;
            }
            if (!match)
                continue;
            matched++;

            foreach (var a in actions)
                a.Apply?.Invoke(pk);
            if (actionScript.Count > 0 && editor.TryModify(pk, [], actionScript).HasFlag(ModifyResult.Error))
                errors++;

            // Legalizar fica por ultimo: o resultado final e o que precisa passar na analise.
            if (legalize && !pk.IsEgg && CoreAdapter.IsLegal(pk) == false)
            {
                var result = EncounterDatabase.Legalize(sav, pk.Clone(), out _);
                if (result is not null && CoreAdapter.IsLegal(result) == true)
                {
                    pk = result;
                    legalized++;
                }
                else
                    legalizeFailed++;
            }

            pk.ResetPartyStats();
            pk.RefreshChecksum();
            var before = original.Clone();
            before.ResetPartyStats();
            before.RefreshChecksum();
            if (pk.Data.SequenceEqual(before.Data))
                continue;
            changes.Add(new BatchChange(t, pk, CoreAdapter.IsLegal(original), CoreAdapter.IsLegal(pk)));
        }
        return new BatchRun(checkedCount, matched, changes, errors, legalizeFailed, legalized);
    }
}
