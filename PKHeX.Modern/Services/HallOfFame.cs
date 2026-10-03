using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Pokemon de uma equipe do Hall da Fama (o que o jogo guarda: especie, forma, apelido, nivel...).</summary>
public sealed record FameMember(ushort Species, byte Form, int Gender, bool Shiny, string Nickname, int Level);

/// <summary>Equipe registrada no Hall da Fama ("Primeira vitória", "Vitória nº 12"...), com a data quando o jogo guarda.</summary>
public sealed record FameTeam(string Title, string Date, IReadOnlyList<FameMember> Members);

/// <summary>
/// Hall da Fama (so leitura): Gen 1 (50 equipes), Gen 3 (50 equipes), X/Y e Omega Ruby/Alpha Sapphire (primeira vitoria e
/// as 15 mais recentes, com data) e Sun/Moon/Ultra (especies da primeira equipe e da atual).
/// </summary>
public static class HallOfFame
{
    public static IReadOnlyList<FameTeam> Load(SaveFile sav)
    {
        try
        {
            return sav switch
            {
                SAV1 s => Load1(s),
                SAV3 s => Load3(s),
                SAV6XY s => Load6(s, s.HallOfFame),
                SAV6AO s => Load6(s, s.HallOfFame),
                SAV7 s => Load7(s),
                _ => [],
            };
        }
        catch
        {
            return []; // save com o bloco do Hall da Fama vazio ou estranho: so nao mostra a aba
        }
    }

    private static string Name(ushort species) => species < GameInfo.Strings.Species.Count ? GameInfo.Strings.Species[species] : $"#{species}";

    private static List<FameTeam> Load1(SAV1 sav)
    {
        var reader = sav.HallOfFame;
        int count = Math.Min((int)sav.HallOfFameCount, HallOfFameReader1.TeamCount);
        List<FameTeam> teams = [];
        for (int t = 0; t < count; t++)
        {
            List<FameMember> members = [];
            int n = reader.GetTeamMemberCount(t);
            for (int i = 0; i < n; i++)
            {
                var e = reader.GetEntity(t, i);
                if (e.Species == 0)
                    continue;
                members.Add(new FameMember(e.Species, 0, 0, false, e.Nickname, e.Level));
            }
            if (members.Count > 0)
                teams.Add(new FameTeam(t == 0 ? "Primeira vitória" : $"Registro {t + 1}", "", members));
        }
        return teams;
    }

    private static List<FameTeam> Load3(SAV3 sav)
    {
        List<FameTeam> teams = [];
        var entries = HallFame3Entry.GetEntries(sav);
        for (int t = 0; t < entries.Length; t++)
        {
            var members = entries[t].Team.Where(p => p.Species != 0)
                .Select(p => new FameMember(p.Species, p.DisplayForm(sav.Version), 0, p.IsShiny, p.Nickname, p.Level)).ToList();
            if (members.Count == 0)
                break; // as equipes ficam no comeco; a primeira vazia encerra a lista
            teams.Add(new FameTeam(t == 0 ? "Primeira vitória" : $"Registro {t + 1}", "", members));
        }
        return teams;
    }

    private static List<FameTeam> Load6(SAV6 sav, HallOfFame6 fame)
    {
        List<FameTeam> teams = [];
        for (int t = 0; t < HallOfFame6.Entries; t++)
        {
            var entry = fame.GetEntry(t);
            var index = new HallFame6Index(entry[^HallFame6Index.SIZE..]);
            if (!index.HasData)
                continue;
            List<FameMember> members = [];
            for (int i = 0; i < HallOfFame6.PokeCount; i++)
            {
                var e = new HallFame6Entity(fame.GetEntity(t, i), sav.Language);
                if (e.Species == 0)
                    continue;
                members.Add(new FameMember(e.Species, e.Form, (int)e.Gender, e.IsShiny, e.Nickname, (int)e.Level));
            }
            if (members.Count == 0)
                continue;
            var date = index.Month is >= 1 and <= 12 && index.Day is >= 1 and <= 31 ? $"{index.Day:00}/{index.Month:00}/{2000 + index.Year}" : "";
            var title = t == 0 ? "Primeira vitória" : $"Vitória nº {index.ClearIndex}";
            teams.Add(new FameTeam(title, date, members));
        }
        return teams;
    }

    private static List<FameTeam> Load7(SAV7 sav)
    {
        var fame = sav.EventWork.Fame;
        List<FameTeam> teams = [];
        foreach (var (title, start) in new[] { ("Primeira vitória", 0), ("Equipe mais recente", 6) })
        {
            var members = Enumerable.Range(start, 6).Select(fame.GetEntry).Where(s => s != 0 && s <= (ushort)sav.MaxSpeciesID)
                .Select(s => new FameMember(s, 0, 0, false, Name(s), 0)).ToList();
            if (members.Count > 0)
                teams.Add(new FameTeam(title, "", members));
        }
        return teams;
    }
}
