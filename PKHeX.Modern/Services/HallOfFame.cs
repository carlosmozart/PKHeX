using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Pokemon de uma equipe do Hall da Fama (o que o jogo guarda: especie, forma, apelido, nivel...).</summary>
/// <param name="Slot">Posicao do Pokemon na equipe gravada no save (para editar o certo).</param>
public sealed record FameMember(ushort Species, byte Form, int Gender, bool Shiny, string Nickname, int Level, int Slot = 0);

/// <summary>Equipe registrada no Hall da Fama ("Primeira vitória", "Vitória nº 12"...), com a data quando o jogo guarda.</summary>
/// <param name="Index">Posicao da equipe no save.</param>
/// <param name="When">Data da vitoria (X/Y e Omega Ruby/Alpha Sapphire).</param>
public sealed record FameTeam(string Title, string Date, IReadOnlyList<FameMember> Members, int Index = 0, DateTime? When = null);

/// <summary>O que o Hall da Fama de cada jogo guarda e permite editar.</summary>
/// <param name="NicknameLength">0 = o jogo nao guarda apelido nem nivel (Sun/Moon so guarda a especie).</param>
public sealed record FameCaps(int NicknameLength, bool HasDate, bool HasShiny, ushort MaxSpecies);

/// <summary>
/// Hall da Fama (ver e editar): Gen 1 (50 equipes), Gen 3 (50 equipes), X/Y e Omega Ruby/Alpha Sapphire (primeira vitoria e
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

    /// <summary>Jogos com Hall da Fama (a aba aparece mesmo sem equipe, para registrar a equipe atual).</summary>
    public static bool IsSupported(SaveFile sav) => sav is SAV1 or SAV3 or SAV6XY or SAV6AO or SAV7;

    public static FameCaps Caps(SaveFile sav) => sav switch
    {
        SAV1 s => new(s.Japanese ? 5 : 10, false, false, 151),
        SAV3 s => new(s.Japanese ? 5 : 10, false, false, (ushort)s.MaxSpeciesID),
        SAV6 => new(12, true, true, (ushort)sav.MaxSpeciesID),
        _ => new(0, false, false, (ushort)sav.MaxSpeciesID),
    };

    /// <summary>Troca especie, apelido e nivel de um Pokemon da equipe (no Sun/Moon so a especie).</summary>
    public static void SetMember(SaveFile sav, int team, int slot, ushort species, string nickname, int level, bool shiny = false)
    {
        level = Math.Clamp(level, 1, 100);
        switch (sav)
        {
            case SAV1 s:
            {
                var e = s.HallOfFame.GetEntity(team, slot);
                e.Species = species; e.Nickname = nickname; e.Level = (byte)level;
                break;
            }
            case SAV3 s:
            {
                var entries = HallFame3Entry.GetEntries(s);
                var m = entries[team].GetMember(slot);
                m.Species = species; m.Nickname = nickname; m.Level = level;
                HallFame3Entry.SetEntries(s, entries);
                break;
            }
            case SAV6XY s: SetMember6(s, s.HallOfFame, team, slot, species, nickname, level, shiny); break;
            case SAV6AO s: SetMember6(s, s.HallOfFame, team, slot, species, nickname, level, shiny); break;
            case SAV7 s: s.EventWork.Fame.SetEntry(team * 6 + slot, species); break;
        }
    }

    private static void SetMember6(SAV6 sav, HallOfFame6 fame, int team, int slot, ushort species, string nickname, int level, bool shiny)
    {
        var e = new HallFame6Entity(fame.GetEntity(team, slot), sav.Language);
        if (e.Species != species)
        {
            e.Species = species;
            e.Form = 0;
            var pi = sav.Personal[species];
            e.Gender = pi.Genderless ? 2u : pi.OnlyFemale ? 1u : pi.OnlyMale ? 0u : Math.Min(e.Gender, 1u);
        }
        var speciesName = SpeciesName.GetSpeciesNameGeneration(species, sav.Language, 6);
        e.Nickname = string.IsNullOrWhiteSpace(nickname) ? speciesName : nickname;
        e.IsNicknamed = e.Nickname != speciesName;
        e.Level = (uint)level;
        e.IsShiny = shiny;
    }

    /// <summary>Data da vitoria (X/Y e Omega Ruby/Alpha Sapphire guardam ano 2000-2255, mes e dia).</summary>
    public static void SetDate(SaveFile sav, int team, DateTime date)
    {
        if (sav is not SAV6 s6 || Fame6(s6) is not { } fame)
            return;
        var index = new HallFame6Index(fame.GetEntry(team)[^HallFame6Index.SIZE..]);
        index.Year = (uint)Math.Clamp(date.Year - 2000, 0, 255);
        index.Month = (uint)date.Month;
        index.Day = (uint)date.Day;
    }

    private static HallOfFame6? Fame6(SAV6 sav) => sav switch { SAV6XY x => x.HallOfFame, SAV6AO a => a.HallOfFame, _ => null };

    /// <summary>Apaga a equipe; as seguintes sobem uma posicao (no Sun/Moon so esvazia).</summary>
    public static void DeleteTeam(SaveFile sav, int team)
    {
        switch (sav)
        {
            case SAV1 s:
                s.HallOfFame.Delete(team);
                if (s.HallOfFameCount > 0) s.HallOfFameCount--;
                break;
            case SAV3 s:
            {
                var entries = HallFame3Entry.GetEntries(s);
                for (int i = team; i < entries.Length - 1; i++)
                    entries[i].CopyFrom(entries[i + 1]);
                entries[^1].Data.Clear();
                HallFame3Entry.SetEntries(s, entries);
                break;
            }
            case SAV6 s when Fame6(s) is { } fame: fame.ClearEntry(team); break;
            case SAV7 s:
                for (int i = 0; i < 6; i++) s.EventWork.Fame.SetEntry(team * 6 + i, 0);
                break;
        }
    }

    /// <summary>Troca a equipe pela equipe atual do save.</summary>
    public static void CopyParty(SaveFile sav, int team)
    {
        var party = Enumerable.Range(0, sav.PartyCount).Select(sav.GetPartySlotAtIndex).Where(p => p.Species != 0 && !p.IsEgg).ToList();
        switch (sav)
        {
            case SAV1 s:
            {
                var reader = s.HallOfFame;
                for (int i = 0; i < HallOfFameReader1.SlotsPerTeam; i++)
                {
                    var e = reader.GetEntity(team, i);
                    if (i < party.Count) e.Register((PK1)party[i]); else e.Clear();
                }
                break;
            }
            case SAV3 s:
            {
                var entries = HallFame3Entry.GetEntries(s);
                entries[team].Data.Clear();
                entries[team].CopyFrom(party);
                HallFame3Entry.SetEntries(s, entries);
                break;
            }
            case SAV6 s when Fame6(s) is { } fame:
                for (int i = 0; i < HallOfFame6.PokeCount; i++)
                {
                    var span = fame.GetEntity(team, i);
                    span.Clear();
                    if (i >= party.Count) continue;
                    var pk = party[i];
                    var e = new HallFame6Entity(span, s.Language)
                    {
                        Species = pk.Species, HeldItem = (ushort)pk.HeldItem,
                        Move1 = pk.Move1, Move2 = pk.Move2, Move3 = pk.Move3, Move4 = pk.Move4,
                        EncryptionConstant = pk.EncryptionConstant, TID16 = pk.TID16, SID16 = pk.SID16,
                    };
                    e.Form = pk.Form; e.Gender = (uint)pk.Gender; e.Level = (uint)pk.CurrentLevel;
                    e.OriginalTrainerGender = pk.OriginalTrainerGender; e.IsShiny = pk.IsShiny;
                    e.IsNicknamed = pk.IsNicknamed; e.Nickname = pk.Nickname; e.OriginalTrainerName = pk.OriginalTrainerName;
                }
                break;
            case SAV7 s:
                for (int i = 0; i < 6; i++) s.EventWork.Fame.SetEntry(team * 6 + i, i < party.Count ? party[i].Species : (ushort)0);
                break;
        }
    }

    /// <summary>Registra a equipe atual como uma vitoria nova (como ao vencer a Liga). Retorna a posicao da equipe.</summary>
    public static int RegisterParty(SaveFile sav)
    {
        switch (sav)
        {
            case SAV1 s:
                s.HallOfFameCount = s.HallOfFame.RegisterParty(s, s.HallOfFameCount);
                return s.HallOfFameCount - 1;
            case SAV3 s:
            {
                var entries = HallFame3Entry.GetEntries(s);
                int team = Array.FindIndex(entries, e => e.GetMember(0).Species == 0);
                if (team < 0)
                {
                    // Cheio: a mais antiga sai e as outras sobem.
                    for (int i = 0; i < entries.Length - 1; i++)
                        entries[i].CopyFrom(entries[i + 1]);
                    team = entries.Length - 1;
                    HallFame3Entry.SetEntries(s, entries);
                }
                CopyParty(s, team);
                return team;
            }
            case SAV6 s when Fame6(s) is { } fame:
            {
                int team = (int)fame.GetInsertIndex(out var clear);
                fame.GetEntry(team).Clear();
                var index = new HallFame6Index(fame.GetEntry(team)[^HallFame6Index.SIZE..]);
                index.HasData = true;
                index.ClearIndex = Math.Max(1, clear);
                CopyParty(s, team);
                SetDate(s, team, DateTime.Today);
                return team;
            }
            case SAV7 s:
            {
                CopyParty(s, 1);
                if (s.EventWork.Fame.GetEntry(0) == 0)
                    CopyParty(s, 0);
                return 1;
            }
        }
        return -1;
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
                members.Add(new FameMember(e.Species, 0, 0, false, e.Nickname, e.Level, i));
            }
            if (members.Count > 0)
                teams.Add(new FameTeam(t == 0 ? "Primeira vitória" : $"Registro {t + 1}", "", members, t));
        }
        return teams;
    }

    private static List<FameTeam> Load3(SAV3 sav)
    {
        List<FameTeam> teams = [];
        var entries = HallFame3Entry.GetEntries(sav);
        for (int t = 0; t < entries.Length; t++)
        {
            var members = entries[t].Team.Select((p, i) => (p, i)).Where(x => x.p.Species != 0)
                .Select(x => new FameMember(x.p.Species, x.p.DisplayForm(sav.Version), 0, x.p.IsShiny, x.p.Nickname, x.p.Level, x.i)).ToList();
            if (members.Count == 0)
                break; // as equipes ficam no comeco; a primeira vazia encerra a lista
            teams.Add(new FameTeam(t == 0 ? "Primeira vitória" : $"Registro {t + 1}", "", members, t));
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
                members.Add(new FameMember(e.Species, e.Form, (int)e.Gender, e.IsShiny, e.Nickname, (int)e.Level, i));
            }
            if (members.Count == 0)
                continue;
            DateTime? when = index.Month is >= 1 and <= 12 && index.Day >= 1 && index.Day <= DateTime.DaysInMonth(2000 + (int)index.Year, (int)index.Month)
                ? new DateTime(2000 + (int)index.Year, (int)index.Month, (int)index.Day) : null;
            var date = when is { } w ? $"{w:dd/MM/yyyy}" : "";
            var title = t == 0 ? "Primeira vitória" : $"Vitória nº {index.ClearIndex}";
            teams.Add(new FameTeam(title, date, members, t, when));
        }
        return teams;
    }

    private static List<FameTeam> Load7(SAV7 sav)
    {
        var fame = sav.EventWork.Fame;
        List<FameTeam> teams = [];
        foreach (var (title, team) in new[] { ("Primeira vitória", 0), ("Equipe mais recente", 1) })
        {
            var members = Enumerable.Range(0, 6).Select(i => (s: fame.GetEntry(team * 6 + i), i)).Where(x => x.s != 0 && x.s <= (ushort)sav.MaxSpeciesID)
                .Select(x => new FameMember(x.s, 0, 0, false, Name(x.s), 0, x.i)).ToList();
            if (members.Count > 0)
                teams.Add(new FameTeam(title, "", members, team));
        }
        return teams;
    }
}
