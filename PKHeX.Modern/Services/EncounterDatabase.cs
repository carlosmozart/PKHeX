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
    /// <remarks>
    /// O banco de Mystery Gift do PKHeX (<see cref="EncounterEvent.GetAllEvents"/>) so tem eventos da Gen 4 em diante.
    /// Os eventos da Gen 1-3 (WC3, Colosseum/XD, PCNY, Mew/Celebi) ficam em listas internas do Core; chegamos neles
    /// pelo gerador de encontros filtrado so por "Mystery", como faz o banco de encontros do PKHeX.
    /// Eventos da propria geracao do save vem primeiro.
    /// </remarks>
    public static IReadOnlyList<IEncounterInfo> LoadGifts(SaveFile sav, bool onlyThisGame, CancellationToken token = default)
    {
        var result = new List<IEncounterInfo>();
        if (sav.Generation <= 3)
            result.AddRange(GetClassicGifts(sav, token));

        if (sav.Generation >= 4 || !onlyThisGame)
        {
            IEnumerable<MysteryGift> db = EncounterEvent.GetAllEvents();
            if (onlyThisGame && EntityPresenceFilters.GetFilterGift<MysteryGift>(sav.Context, sav.Generation) is { } filter)
                db = db.Where(filter);
            foreach (var mg in db)
            {
                mg.GiftUsed = false;
                result.Add(mg);
            }
        }
        return [.. result.OrderBy(g => g.Generation == sav.Generation ? 0 : 1)]; // estavel: mantem a ordem dentro de cada grupo
    }

    /// <summary>Eventos da Gen 1-3 compativeis com o save, via gerador de encontros (tipo Mystery).</summary>
    private static List<IEncounterInfo> GetClassicGifts(SaveFile sav, CancellationToken token)
    {
        var settings = new SearchSettings { Context = sav.Context, Generation = sav.Generation, Species = 0 };
        var versions = settings.GetVersions(sav);
        var pk = sav.BlankPKM;
        var seen = new HashSet<IEncounterInfo>(ReferenceEqualityComparer.Instance);
        var result = new List<IEncounterInfo>();
        try
        {
            EncounterMovesetGenerator.PriorityList = [EncounterTypeGroup.Mystery];
            for (ushort species = 1; species <= sav.MaxSpeciesID; species++)
            {
                token.ThrowIfCancellationRequested();
                pk.Species = species;
                pk.Form = 0;
                pk.SetGender(pk.GetSaneGender());
                EncounterMovesetGenerator.OptimizeCriteria(pk, sav);
                foreach (var enc in EncounterMovesetGenerator.GenerateEncounters(pk, ReadOnlyMemory<ushort>.Empty, versions))
                {
                    if (enc is not IEncounterEgg && seen.Add(enc))
                        result.Add(enc);
                }
            }
        }
        finally
        {
            EncounterMovesetGenerator.ResetFilters();
        }
        return result;
    }

    /// <summary>Gera o Pokemon do encontro/gift ja no formato do save. Retorna null e o erro se nao der para converter.</summary>
    public static PKM? ToEntity(SaveFile sav, IEncounterInfo enc, out string? error)
        => ToEntity(sav, enc, EncounterCriteria.Unrestricted, out error);

    public static PKM? ToEntity(SaveFile sav, IEncounterInfo enc, EncounterCriteria criteria, out string? error)
    {
        error = null;
        try
        {
            var temp = enc.ConvertToPKM(sav, criteria);
            var pk = EntityConverter.ConvertToType(temp, sav.PKMType, out var result);
            if (pk is null)
            {
                error = result.GetDisplayString(temp, sav.PKMType);
                return null;
            }
            sav.AdaptToSaveFile(pk);
            // Vindo de outra geracao para a Gen 8+, ou presente distribuido pelo HOME (card 9000+), o jogo exige o
            // rastreador do HOME (senao: "Pokemon HOME Transfer Tracker is missing").
            bool needsTracker = enc.Context != pk.Context || enc is MysteryGift { CardID: >= 9000 };
            if (pk is IHomeTrack { Tracker: 0 } home && needsTracker && pk.Format >= 8)
                home.Tracker = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
            pk.RefreshChecksum();
            return pk;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Legalizar: gera de novo o Pokemon a partir de um encontro real deste jogo (com o metodo de PID/IV correto
    /// de cada geracao) e reaplica o que o usuario escolheu: natureza, genero, shiny, nivel, item, apelido e golpes.
    /// Devolve o primeiro resultado legal, ou null com o motivo.
    /// </summary>
    /// <param name="only">Encontro escolhido pelo usuario (modo legal, "Trocar encontro"); null = procura o melhor.</param>
    public static PKM? Legalize(SaveFile sav, PKM current, out string message, CancellationToken token = default, IEncounterInfo? only = null)
    {
        var encounters = only is not null ? [only] : SearchEncounters(sav, current.Species, onlyThisGame: true, token)
            .Where(e => e.Form == current.Form || e is MysteryGift)
            .OrderBy(e => GetPreference(e, current))
            .Take(25)
            .ToList();
        if (encounters.Count == 0)
        {
            message = "nenhum encontro possível para esta espécie neste jogo";
            return null;
        }

        var shiny = current.IsShiny ? Shiny.Always : Shiny.Never;
        var gender = current.PersonalInfo.IsDualGender ? (Gender)current.Gender : Gender.Random;
        EncounterCriteria[] attempts =
        [
            new() { Nature = current.Nature, Gender = gender, Shiny = shiny },
            new() { Nature = current.Nature, Shiny = shiny },
            EncounterCriteria.Unrestricted,
        ];

        string? lastProblem = null;
        (PKM Pk, IEncounterInfo Enc)? fallback = null; // legal, mas com a sequencia RNG marcada como suspeita
        foreach (var enc in encounters)
        {
            foreach (var criteria in attempts)
            {
                token.ThrowIfCancellationRequested();
                if (ToEntity(sav, enc, criteria, out _) is not { } pk)
                    continue;
                // Shiny selvagem da Gen 3: o gerador do Core quebra a sequencia do jogo; refazemos do jeito do jogo.
                if (criteria.Shiny == Shiny.Always && enc is IEncounterSlot3 slot3 && pk is PK3 pk3
                    && !ShinyMethodH.TrySetShiny(pk3, slot3, criteria.Nature, criteria.Gender, TimeSpan.FromSeconds(3)))
                    continue;
                CarryOver(current, pk, enc);
                var la = new LegalityAnalysis(pk);
                if (la.Valid)
                {
                    if (la.Info.FrameMatches)
                    {
                        message = Describe(enc, current, pk);
                        return pk;
                    }
                    fallback ??= (pk, enc);
                    continue;
                }
                lastProblem ??= CoreAdapter.GetLegalityIssues(pk, 1).FirstOrDefault();
            }
        }
        if (fallback is { } f)
        {
            message = Describe(f.Enc, current, f.Pk) + " (aviso: a sequência RNG do jogo não confere, marcado como suspeito)";
            return f.Pk;
        }
        message = "nenhum encontro gerou um Pokémon legal" + (lastProblem is null ? "" : $" ({lastProblem})");
        return null;
    }

    /// <summary>Ordem de preferencia: mesma versao do save, selvagem/estatico, nivel ate o atual; eventos e trocas por ultimo.</summary>
    private static int GetPreference(IEncounterInfo enc, PKM current)
    {
        int score = 0;
        if (enc is MysteryGift)
            score += 100;
        if (enc is IFixedTrainer { IsFixedTrainer: true })
            score += 50; // troca: OT de outro treinador
        if (enc.IsEgg)
            score += 10;
        if (enc.LevelMin > current.CurrentLevel)
            score += 30;
        if (enc.Version != current.Version)
            score += 5;
        return score;
    }

    /// <summary>Reaplica no Pokemon gerado o que o usuario tinha escolhido, quando for compativel com o encontro.</summary>
    private static void CarryOver(PKM from, PKM to, IEncounterInfo enc)
    {
        // O encontro pode ser de uma pre-evolucao (ex.: Dratini para um Dragonite): evolui ate a especie escolhida.
        if (to.Species != from.Species || to.Form != from.Form)
        {
            bool nicknamed = to.IsNicknamed;
            int abilitySlot = to.AbilityNumber switch { 2 => 1, 4 => 2, _ => 0 };
            to.Species = from.Species;
            to.Form = from.Form;
            if (!nicknamed)
                to.ClearNickname();
            to.RefreshAbility(abilitySlot);
            if (from.Format >= 6)
                to.Gender = to.GetSaneGender(); // ate a Gen 5 o genero vem do PID e nao muda ao evoluir
        }
        if (from.CurrentLevel >= enc.LevelMin && from.CurrentLevel > to.CurrentLevel)
            to.CurrentLevel = from.CurrentLevel;
        if (from.HeldItem != 0)
            to.HeldItem = from.HeldItem;
        if (from.IsNicknamed && !to.IsNicknamed)
            to.SetNickname(from.Nickname);

        // Golpes: mantem os do usuario se forem legais nesse encontro; senao, um conjunto legal sugerido.
        Span<ushort> moves = stackalloc ushort[4];
        from.GetMoves(moves);
        if (moves.ContainsAnyExcept((ushort)0))
        {
            to.SetMoves(moves);
            if (!new LegalityAnalysis(to).Info.Moves.All(m => m.Valid))
                to.SetMoveset();
        }
        to.HealPP();
        to.RefreshChecksum();
    }

    private static string Describe(IEncounterInfo enc, PKM before, PKM after)
    {
        var kind = enc is IEncounterable e ? e.LongName : "encontro";
        var where = GetLocationName(enc);
        var lost = new List<string>();
        if (after.Nature != before.Nature) lost.Add("natureza");
        if (after.IsShiny != before.IsShiny) lost.Add("shiny");
        if (after.CurrentLevel != before.CurrentLevel) lost.Add("nível");
        return $"{kind}" + (where.Length > 0 ? $" em {where}" : "") + (lost.Count > 0 ? $"; mudou: {string.Join(", ", lost)}" : "");
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
    /// <summary>Rotulo curto de um encontro: local, tipo e nivel (lista "Trocar encontro" do editor).</summary>
    public static string GetShortLabel(IEncounterInfo enc)
    {
        var kind = enc switch
        {
            MysteryGift g when !string.IsNullOrWhiteSpace(g.CardTitle) => g.CardTitle.Replace('　', ' ').Trim(),
            IEncounterable e => e.LongName,
            _ => "",
        };
        var level = enc.LevelMin == enc.LevelMax ? $"Nv. {enc.LevelMin}" : $"Nv. {enc.LevelMin}–{enc.LevelMax}";
        var where = GetLocationName(enc);
        return string.Join(" · ", new[] { where, kind, level, GetVersionName(enc.Version) }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    public static string GetDetails(IEncounterInfo enc)
    {
        try { return string.Join(Environment.NewLine, enc.GetTextLines()); }
        catch { return ""; }
    }
}
