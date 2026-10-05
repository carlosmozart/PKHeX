using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

public sealed record LegalityTopic(string Name, bool Invalid, IReadOnlyList<string> Issues);

/// <summary>Groups structured Core checks, including current and relearn move slots.</summary>
public static class GroupedLegality
{
    public static IReadOnlyList<LegalityTopic> Analyze(PKM pk, LegalityAnalysis? analysis = null)
    {
        try
        {
            var la = analysis ?? new LegalityAnalysis(pk);
            var ctx = LegalityLocalizationContext.Create(la, GameInfo.CurrentLanguage);
            var items = new List<(string Name, bool Invalid, string Text)>();
            foreach (var c in la.Results)
                if (c.Judgement is Severity.Invalid or Severity.Fishy)
                    items.Add((Topic(c.Identifier), c.Judgement == Severity.Invalid, ctx.Humanize(c)));
            for (int i = 0; i < la.Info.Moves.Length; i++)
                if (!la.Info.Moves[i].Valid) items.Add(("Golpes", true, ctx.FormatMove(la.Info.Moves[i], i + 1, pk.Context)));
            for (int i = 0; i < la.Info.Relearn.Length; i++)
                if (!la.Info.Relearn[i].Valid) items.Add(("Golpes", true, ctx.FormatMove(la.Info.Relearn[i], i + 1, pk.Context)));
            if (!la.Valid && !items.Any(c => c.Invalid)) items.Add(("Outros", true, la.Report()));
            return items.GroupBy(x => x.Name).Select(g => new LegalityTopic(g.Key, g.Any(x => x.Invalid),
                    g.OrderByDescending(x => x.Invalid).Select(x => x.Text).Distinct().ToArray()))
                .OrderByDescending(g => g.Invalid).ThenByDescending(g => g.Issues.Count).ThenBy(g => g.Name).ToArray();
        }
        catch (Exception ex) { return [new("Outros", true, [ex.Message])]; }
    }

    public static string Topic(CheckIdentifier id) => id switch
    {
        CheckIdentifier.Encounter or CheckIdentifier.Egg or CheckIdentifier.Form or CheckIdentifier.Fateful or CheckIdentifier.Evolution or CheckIdentifier.GameOrigin => "Encontro",
        CheckIdentifier.CurrentMove or CheckIdentifier.RelearnMove => "Golpes",
        CheckIdentifier.Ability => "Habilidade", CheckIdentifier.Ball => "Bola",
        CheckIdentifier.Level => "Nível e experiência",
        CheckIdentifier.Trainer or CheckIdentifier.Language or CheckIdentifier.Handler or CheckIdentifier.Geography => "Treinador",
        CheckIdentifier.Ribbon or CheckIdentifier.RibbonMark => "Fitas e marcas",
        CheckIdentifier.PID or CheckIdentifier.EC or CheckIdentifier.Shiny or CheckIdentifier.IVs => "Shiny e PID",
        CheckIdentifier.Memory => "Memórias", _ => "Outros",
    };
}
