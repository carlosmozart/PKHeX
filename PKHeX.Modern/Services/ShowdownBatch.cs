using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Um set de uma equipe colada: o Pokemon pronto (ou null, com o motivo) e se ficou legal.</summary>
public sealed record ShowdownBatchItem(string Name, PKM? Pk, bool Legal, string Note);

/// <summary>
/// Equipes Showdown: varios sets separados por linha em branco (o formato do Showdown e do PokePaste).
/// Cada set passa pelo mesmo caminho do "Colar Showdown" do editor (qualquer idioma).
/// </summary>
public static class ShowdownBatch
{
    /// <summary>Separa os sets. Linhas "=== [gen9] Nome ===" do Teambuilder sao ignoradas.</summary>
    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var sets = new List<string>();
        var current = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("===", StringComparison.Ordinal))
                line = "";
            if (line.Length == 0)
            {
                if (current.Count > 0) sets.Add(string.Join("\n", current));
                current.Clear();
                continue;
            }
            current.Add(line);
        }
        if (current.Count > 0) sets.Add(string.Join("\n", current));
        return sets;
    }

    /// <summary>
    /// Monta o Pokemon do set para o save. No modo legal, um set ilegal e gerado de novo a partir de um encontro
    /// real do jogo (como o Legalizar); se nao der, fica de fora com o motivo.
    /// </summary>
    public static ShowdownBatchItem Build(SaveFile sav, string block, bool legalMode)
    {
        var first = block.Split('\n')[0].Trim();
        try
        {
            // Confere a especie antes de aplicar: um formato antigo corta numeros maiores (Sprigatito virava Genesect na Gen 5).
            if (!ShowdownParsing.TryParseAnyLanguage(block, out var set) || set.Species == 0)
                return new(first, null, false, "texto Showdown inválido");
            var name = set.Species < CoreAdapter.SpeciesNames.Count ? CoreAdapter.SpeciesNames[set.Species] : first;
            if (set.Species > sav.MaxSpeciesID || !sav.Personal.IsPresentInGame(set.Species, set.Form))
                return new(name, null, false, "não existe neste jogo");
            var pk = CoreAdapter.CreateBlank(sav);
            var error = CoreAdapter.ApplyShowdown(pk, block);
            pk.RefreshChecksum();
            if (CoreAdapter.IsLegal(pk) == true)
                return new(name, pk, true, error ?? "");
            if (!legalMode)
                return new(name, pk, false, "ilegal (modo legal desligado)");
            var legal = EncounterDatabase.Legalize(sav, pk.Clone(), out var message);
            return legal is not null && CoreAdapter.IsLegal(legal) == true
                ? new(name, legal, true, Loc.T("legalizado a partir de: ") + message)
                : new(name, null, false, "ilegal e sem encontro legal neste jogo");
        }
        catch (Exception ex)
        {
            return new(first, null, false, ex.Message);
        }
    }

    /// <summary>Texto Showdown de varios Pokemon, separados por linha em branco (vazios e ovos ficam de fora).</summary>
    public static string Export(IEnumerable<PKM> pokemon)
        => string.Join(Environment.NewLine + Environment.NewLine,
            pokemon.Where(p => p.Species > 0 && !p.IsEgg).Select(p => CoreAdapter.ToShowdown(p).Trim()));
}
