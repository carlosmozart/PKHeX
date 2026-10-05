using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;
using static PKHeX.Core.EvolutionType;

namespace PKHeX.Modern.Services;

public sealed record AssistedEvolutionOption(EvolutionMethod Method, byte Form, string Name, string Requirement, string? Blocked, bool Ready);

/// <summary>Evolution choices for the active game. Never edits the source or the world's state.</summary>
public static class AssistedEvolution
{
    private static string T(string text) => Loc.T(text);
    private static string F(string text, object value) => string.Format(T(text), value);

    public static IReadOnlyList<AssistedEvolutionOption> List(PKM source, SaveFile save)
    {
        if (source.Species == 0 || source.IsEgg) return [];
        var pk = source.Clone();
        RemoveEverstone(pk);
        var methods = EvolutionTree.GetEvolutionTree(save.Context).Forward.GetForward(pk.Species, pk.Form).ToArray();
        var friendships = CoreAdapter.GetFriendshipEvolutions(pk, save);
        var beauty = CoreAdapter.GetBeautyEvolutions(pk, save);
        var trades = CoreAdapter.GetTradeEvolutions(pk);
        var items = CoreAdapter.GetItemEvolutions(pk);
        var result = new List<AssistedEvolutionOption>();
        foreach (var original in methods)
        {
            byte form = original.GetDestinationForm(pk.Form);
            if (!save.Personal.IsPresentInGame(original.Species, form)) continue;
            var m = original;
            var friendship = friendships.FirstOrDefault(e => e.Species == m.Species && e.Form == form);
            // The Gen 2 table encodes friendship as LevelUp.
            if (save.Context == EntityContext.Gen2 && friendship is not null) m = m with { Method = friendship.Method };
            string requirement = Requirement(pk, m);
            string? blocked = null;
            bool ready = false;
            if (friendship is not null && m.Method is LevelUpFriendship or LevelUpFriendshipMorning or LevelUpFriendshipNight or LevelUpAffection50MoveType)
            {
                requirement = T(friendship.Requirement);
                blocked = friendship.Blocked;
            }
            else if (m.Method == LevelUpBeauty)
            {
                var b = beauty.FirstOrDefault(e => e.Species == m.Species && e.Form == form);
                requirement = b is null ? requirement : T(b.Requirement);
                blocked = b?.Blocked ?? (b is null ? T("Este jogo não oferece evolução por Beauty.") : null);
            }
            else if (m.Method.IsTrade)
            {
                var trade = trades.FirstOrDefault(e => e.Species == m.Species && e.Form == form);
                if (trade is not null) requirement = T(trade.Requirement);
            }
            else
            {
                var item = items.FirstOrDefault(e => e.Species == m.Species && e.Form == form);
                if (item is not null) { requirement = T(item.Requirement); blocked = item.Blocked; }
                blocked ??= Constraint(pk, save, m);
                ready = blocked is null && Ready(pk, m);
            }
            if (CoreAdapter.GetHeldItemName(source) == "Everstone")
            {
                requirement += T(" · remover Everstone");
                ready = false;
            }
            if (m.Level > 0 && !requirement.Contains(T("Nível"), StringComparison.OrdinalIgnoreCase))
                requirement += " · " + F("Nível {0}", m.Level);
            var forms = FormConverter.GetFormList(m.Species, GameInfo.Strings.types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, save.Context);
            string name = CoreAdapter.SpeciesNames[m.Species] + (forms.Length > 1 && form < forms.Length ? " · " + forms[form] : "");
            result.Add(new(m, form, name, requirement, blocked is null ? null : T(blocked), ready));
        }
        return result;
    }

    private static string? Constraint(PKM pk, SaveFile save, EvolutionMethod m)
    {
        bool female = m.Method is UseItemFemale or LevelUpFemale or LevelUpFormFemale1 or LevelUpRecoilDamageFemale;
        bool male = m.Method is UseItemMale or LevelUpMale or LevelUpRecoilDamageMale;
        if (female && pk.Gender != 1 || male && pk.Gender != 0) return T("O gênero não corresponde a esta evolução.");
        if (m.Method == LevelUpFormFemale1 && pk.Form != 1) return T("A forma não corresponde a esta evolução.");
        if (m.Method.IsLevelUpRequired && pk.CurrentLevel == 100 && pk.Format < 8)
            return T("precisa subir de nível e já está no nível 100");
        if (m.Method is LevelUpKnowMove or LevelUpKnowMoveEC100 or LevelUpKnowMoveECElse && !pk.Moves.Contains(m.Argument))
            return F("Precisa aprender {0} antes de evoluir.", CoreAdapter.MoveNames[m.Argument]);
        if (m.Method == LevelUpAffection50MoveType && !pk.Moves.Any(v => v != 0 && MoveInfo.GetType(v, pk.Context) == m.Argument))
            return F("Precisa aprender um golpe do tipo {0}.", GameInfo.Strings.types[m.Argument]);
        if (m.Method is LevelUpECl5 or LevelUpECgeq5)
        {
            uint pivot = pk.Format <= 5 ? pk.PID : pk.EncryptionConstant;
            bool silcoon = (pivot >> 16) % 10 < 5;
            if (silcoon != (m.Method == LevelUpECl5)) return T("O PID/EC determina a outra evolução de Wurmple.");
        }
        if (m.Method is LevelUpInBattleEC100 or LevelUpKnowMoveEC100 or LevelUpInBattleECElse or LevelUpKnowMoveECElse)
        {
            bool rare = pk.EncryptionConstant % 100 == 0;
            if (rare != (m.Method is LevelUpInBattleEC100 or LevelUpKnowMoveEC100)) return T("O EC determina a outra forma evoluída.");
        }
        if (m.Method is LevelUpATK or LevelUpAeqD or LevelUpDEF)
        {
            var grown = pk.Clone(); grown.CurrentLevel = NextLevel(pk, m);
            var stats = CoreAdapter.GetFinalStats(grown);
            bool valid = m.Method switch { LevelUpATK => stats[1] > stats[2], LevelUpDEF => stats[1] < stats[2], _ => stats[1] == stats[2] };
            if (!valid) return T("Os atributos no nível da evolução escolhem outro resultado.");
        }
        if (m.Method is LevelUpNatureAmped or LevelUpNatureLowKey && ToxtricityUtil.GetAmpLowKeyResult(pk.Nature) != m.GetDestinationForm(pk.Form))
            return T("A natureza determina a outra forma de Toxtricity.");
        if (m.Method is LevelUpVersion or LevelUpVersionDay or LevelUpVersionNight && ((byte)save.Version & 1) != (m.Argument & 1))
            return T("Esta evolução exige a outra versão do jogo.");
        if (m.Method == LevelUpElectric && save.Version is GameVersion.HG or GameVersion.SS)
            return T("Este jogo não possui o local magnético necessário.");
        if (m.Method is LevelUpForest or LevelUpCold && save.Version is GameVersion.HG or GameVersion.SS)
            return T("Este jogo não possui a rocha necessária.");
        if (m.Method is None or UNUSED or Invalid or Hisui) return T("Este método não tem suporte para evolução assistida.");
        return null;
    }

    private static byte NextLevel(PKM pk, EvolutionMethod m)
        => (byte)Math.Min(100, Math.Max(m.Level, pk.CurrentLevel + (m.Method.IsLevelUpRequired && pk.Context is not (EntityContext.Gen8a or EntityContext.Gen9a) ? 1 : 0)));

    private static bool Ready(PKM pk, EvolutionMethod m) => m.Method switch
    {
        LevelUpKnowMove or LevelUpKnowMoveEC100 or LevelUpKnowMoveECElse => pk.Moves.Contains(m.Argument) && pk.CurrentLevel >= m.Level,
        LevelUp or LevelUpMale or LevelUpFemale or LevelUpATK or LevelUpAeqD or LevelUpDEF or LevelUpECl5 or LevelUpECgeq5 or LevelUpNinjask => pk.CurrentLevel >= Math.Max(1, (int)m.Level - 1),
        _ => false,
    };

    public static PKM Build(PKM source, SaveFile save, AssistedEvolutionOption choice)
    {
        var current = List(source, save).FirstOrDefault(e => e.Method == choice.Method && e.Form == choice.Form)
            ?? throw new InvalidOperationException(T("Esta evolução não está disponível."));
        if (current.Blocked is not null) throw new InvalidOperationException(current.Blocked);
        var pk = source.Clone();
        RemoveEverstone(pk);
        var m = current.Method;
        if (m.Method.IsTrade)
        {
            var trade = CoreAdapter.GetTradeEvolutions(pk).First(e => e.Species == m.Species && e.Form == choice.Form);
            CoreAdapter.EvolveByTrade(pk, trade, save);
        }
        else if (m.Method is LevelUpFriendship or LevelUpFriendshipMorning or LevelUpFriendshipNight or LevelUpAffection50MoveType)
            CoreAdapter.EvolveByFriendship(pk, CoreAdapter.GetFriendshipEvolutions(pk, save).First(e => e.Species == m.Species && e.Form == choice.Form), save);
        else if (m.Method == LevelUpBeauty)
            CoreAdapter.EvolveByBeauty(pk, CoreAdapter.GetBeautyEvolutions(pk, save).First(e => e.Species == m.Species && e.Form == choice.Form), save);
        else
        {
            pk.CurrentLevel = NextLevel(pk, m);
            if (m.Method is LevelUpHeldItemDay or LevelUpHeldItemNight && pk.HeldItem == m.Argument) pk.HeldItem = 0;
            if (m.Method == Spin && SweetDecoration(pk) is not null) pk.HeldItem = 0;
            // The extra Shedinja is chosen explicitly; don't insert another Pokemon into the party.
            CoreAdapter.EvolveByItem(pk, new(m.Species, choice.Form, choice.Name, choice.Requirement, 0, null));
            if (m.Method == LevelUpShedinja) pk.Ball = 4;
            if (pk is IFormArgument argument)
                argument.FormArgument = m.Species switch
                {
                    (ushort)Species.Runerigus => Math.Max(argument.FormArgument, 49),
                    (ushort)Species.Wyrdeer or (ushort)Species.Annihilape => Math.Max(argument.FormArgument, 20),
                    (ushort)Species.Overqwil => pk.Context is EntityContext.Gen8a or EntityContext.Gen9a ? Math.Max(argument.FormArgument, 20) : 0,
                    (ushort)Species.Basculegion => Math.Max(argument.FormArgument, 294),
                    (ushort)Species.Kingambit => Math.Max(argument.FormArgument, 3),
                    (ushort)Species.Alcremie => SweetDecoration(source) ?? 0,
                    (ushort)Species.Sirfetchd => pk is PA9 ? Math.Max(argument.FormArgument, 3) : 0,
                    (ushort)Species.Gholdengo => pk is PA9 ? 0u : 999u,
                    _ => 0,
                };
        }
        if (pk.PersonalInfo.Gender == 255) pk.Gender = 2;
        else if (pk.PersonalInfo.Gender == 254) pk.Gender = 1;
        else if (pk.PersonalInfo.Gender == 0) pk.Gender = 0;
        if (pk is PA9 pa9 && !m.Method.IsTrade)
        {
            pk.Ability = source.Ability;
            pk.AbilityNumber = source.AbilityNumber;
            pa9.SetPlusFlags(pa9.PersonalInfo, new LegalityAnalysis(pa9), false, false);
        }
        pk.RefreshChecksum();
        return pk;
    }

    private static void RemoveEverstone(PKM pk) { if (CoreAdapter.GetHeldItemName(pk) == "Everstone") pk.HeldItem = 0; }

    private static uint? SweetDecoration(PKM pk)
    {
        var held = CoreAdapter.GetHeldItemName(pk);
        foreach (var decoration in Enum.GetValues<AlcremieDecoration>())
            if (held == decoration + " Sweet") return (uint)decoration;
        return null;
    }

    private static string Requirement(PKM pk, EvolutionMethod m)
    {
        string item = m.Argument < CoreAdapter.GetItemNames(pk).Count ? CoreAdapter.GetItemNames(pk)[m.Argument] : m.Argument.ToString();
        string move = m.Argument < CoreAdapter.MoveNames.Count ? CoreAdapter.MoveNames[m.Argument] : m.Argument.ToString();
        return m.Method switch
        {
            LevelUp => F("Nível {0}", m.Level),
            LevelUpMale => F("Nível {0}, macho", m.Level), LevelUpFemale or LevelUpFormFemale1 => F("Nível {0}, fêmea", m.Level),
            Trade => T("troca"), TradeHeldItem => F("Troca segurando {0}", item), TradeShelmetKarrablast => F("Troca por {0}", pk.Species == (ushort)Species.Shelmet ? "Karrablast" : "Shelmet"),
            UseItem => F("Usar {0}", item), UseItemMale => F("Usar {0}, macho", item), UseItemFemale => F("Usar {0}, fêmea", item),
            UseItemWormhole => F("Usar {0} no Ultra Espaço", item), UseItemFullMoon => F("Usar {0} na lua cheia", item),
            LevelUpHeldItemDay => F("Segurando {0}, de dia", item), LevelUpHeldItemNight => F("Segurando {0}, à noite", item),
            LevelUpKnowMove or LevelUpKnowMoveEC100 or LevelUpKnowMoveECElse => F("Sabendo {0}", move),
            LevelUpWithTeammate => F("Com {0} na equipe", CoreAdapter.SpeciesNames[m.Argument]),
            // Method 30 is Pancham's Dark-type party member, despite the historical enum name.
            LevelUpMoveType => T("Com um Pokémon Dark na equipe"),
            LevelUpFriendship => T("Felicidade alta"), LevelUpFriendshipMorning => T("Felicidade alta, de dia"), LevelUpFriendshipNight => T("Felicidade alta, à noite"),
            LevelUpAffection50MoveType => T("Felicidade/carinho e golpe Fairy"), LevelUpBeauty => F("Beauty ≥ {0}", m.Argument),
            LevelUpATK => F("Nível {0}, Attack > Defense", m.Level), LevelUpAeqD => F("Nível {0}, Attack = Defense", m.Level), LevelUpDEF => F("Nível {0}, Attack < Defense", m.Level),
            LevelUpECl5 => F("Nível {0}, PID/EC escolhe Silcoon", m.Level), LevelUpECgeq5 => F("Nível {0}, PID/EC escolhe Cascoon", m.Level),
            LevelUpNinjask => F("Nível {0}", m.Level), LevelUpShedinja => T("Nível 20, vaga na equipe e Poké Ball; escolhe apenas Shedinja"),
            LevelUpElectric => T("Em área magnética (ex.: Mt. Coronet)"), LevelUpForest => T("Perto de Moss Rock"), LevelUpCold => T("Perto de Ice Rock"),
            LevelUpInverted => F("Nível {0}, aparelho de cabeça para baixo", m.Level), LevelUpWeather => T("Subir de nível com chuva"),
            LevelUpMorning => F("Nível {0}, de dia", m.Level), LevelUpNight => F("Nível {0}, à noite", m.Level), LevelUpDusk => F("Nível {0}, ao entardecer", m.Level),
            LevelUpVersion => F("Nível {0}, na versão exigida", m.Level),
            LevelUpVersionDay => F("Nível {0}, na versão exigida, de dia", m.Level),
            LevelUpVersionNight => F("Nível {0}, na versão exigida, à noite", m.Level),
            LevelUpSummit => T("Subir de nível em Mount Lanakila"), LevelUpWormhole => T("Subir de nível no Ultra Espaço"),
            CriticalHitsInBattle => T("Três acertos críticos na mesma batalha"), HitPointsLostInBattle => T("Perder 49 HP e passar sob o arco em Dusty Bowl"),
            Spin => T("Girar segurando um Sweet"),
            LevelUpNatureAmped => F("Nível {0}, natureza para Amped Form", m.Level), LevelUpNatureLowKey => F("Nível {0}, natureza para Low Key Form", m.Level),
            TowerOfDarkness => T("Tower of Darkness"), TowerOfWaters => T("Tower of Waters"),
            LevelUpWalkStepsWith => F("Caminhar {0} passos e subir de nível", m.Argument), LevelUpUnionCircle => T("Subir de nível no Union Circle"),
            LevelUpInBattleEC100 or LevelUpInBattleECElse => F("Nível {0}, em batalha; EC escolhe a forma", m.Level),
            LevelUpCollect999 => T("Coletar 999 Gimmighoul Coins"), LevelUpDefeatEquals => T("Derrotar três Bisharp de grupos segurando Leader's Crest"),
            LevelUpUseMoveSpecial => T("Usar Rage Fist 20 vezes e subir de nível"),
            LevelUpRecoilDamageMale => T("Macho, acumular 294 de dano de recuo"), LevelUpRecoilDamageFemale => T("Fêmea, acumular 294 de dano de recuo"),
            UseMoveAgileStyle => T("Usar Psyshield Bash 20 vezes em Agile Style"), UseMoveStrongStyle => T("Usar Barb Barrage 20 vezes em Strong Style"), UseMoveBarbBarrage => T("Usar Barb Barrage 20 vezes"),
            _ => T("Este método não tem suporte para evolução assistida."),
        };
    }
}
