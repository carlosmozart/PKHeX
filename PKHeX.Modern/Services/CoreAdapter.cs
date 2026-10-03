using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>
/// Ponte unica entre a interface e o PKHeX.Core.
/// Se uma atualizacao do upstream mudar alguma API, normalmente basta ajustar este arquivo.
/// </summary>
public static class CoreAdapter
{
    public static IReadOnlyList<string> SpeciesNames => GameInfo.Strings.specieslist;
    public static IReadOnlyList<string> MoveNames => GameInfo.Strings.movelist;
    /// <summary>Lista geral de itens (numeracao da Gen 4+). Para um Pokemon ou save, use <see cref="GetItemNames(PKM)"/>.</summary>
    public static IReadOnlyList<string> ItemNames => GameInfo.Strings.itemlist;

    /// <summary>Nomes de itens na numeracao do formato do Pokemon (Gen 1-3 tem numeracao propria).</summary>
    public static IReadOnlyList<string> GetItemNames(PKM pk) => GameInfo.Strings.GetItemStrings(pk.Context, pk.Version);
    public static IReadOnlyList<string> GetItemNames(SaveFile sav) => GameInfo.Strings.GetItemStrings(sav.Context, sav.Version);

    /// <summary>Nome do item que o Pokemon segura ("" se nenhum).</summary>
    public static string GetHeldItemName(PKM pk)
    {
        var names = GetItemNames(pk);
        return pk.HeldItem > 0 && pk.HeldItem < names.Count ? names[pk.HeldItem] : "";
    }

    /// <summary>Itens que podem ser segurados no save carregado (Text/Value), como na lista do PKHeX.</summary>
    public static IReadOnlyList<ComboItem> GetHeldItemOptions() => GameInfo.FilteredSources.Items;
    public static IReadOnlyList<string> NatureNames => GameInfo.Strings.natures;
    public static IReadOnlyList<string> AbilityNames => GameInfo.Strings.abilitylist;

    /// <summary>Bolas disponiveis no save carregado (Text/Value).</summary>
    public static IReadOnlyList<ComboItem> GetBalls() => GameInfo.FilteredSources.Balls;

    /// <summary>Especies que existem no jogo aberto (lista filtrada do Core).</summary>
    public static IReadOnlyList<ComboItem> GetSpeciesInGame() => GameInfo.FilteredSources.Species;

    /// <summary>Bolas legais para o encontro atual do Pokemon (vazio se o encontro nao for reconhecido).</summary>
    public static HashSet<int> GetLegalBalls(PKM pk)
    {
        var result = new HashSet<int>();
        try
        {
            Span<Ball> balls = stackalloc Ball[BallApplicator.MaxBallSpanAlloc];
            var count = BallApplicator.GetLegalBalls(balls, pk);
            foreach (var b in balls[..count])
                result.Add((int)b);
        }
        catch
        {
            // sem analise: nenhuma bola
        }
        return result;
    }

    /// <summary>Locais de encontro validos para a versao/contexto do Pokemon.</summary>
    public static IReadOnlyList<ComboItem> GetMetLocations(PKM pk) => GameInfo.GetLocationList(pk.Version, pk.Context);

    /// <summary>Codigo de idioma do PKHeX (en, ja, fr, it, de, es, ko, zh-Hans, zh-Hant...).</summary>
    public static void SetLanguage(string code, SaveFile? sav = null)
    {
        if (!GameLanguage.IsLanguageValid(code))
            code = GameLanguage.DefaultLanguage;
        // So trocar CurrentLanguage nao basta: os nomes vem de GameInfo.Strings/Sources.
        GameInfo.CurrentLanguage = code;
        GameInfo.Strings = GameInfo.GetStrings(code);
        if (sav is not null)
            GameInfo.FilteredSources = new FilteredGameDataSource(sav, GameInfo.Sources);
    }

    /// <summary>
    /// Nomes de 1 ate <paramref name="max"/> (sem vazios), para os campos com sugestoes enquanto se digita.
    /// O indice 0 ("nenhum") fica de fora; use <see cref="FindIndex"/> para voltar do nome ao indice.
    /// </summary>
    public static IReadOnlyList<string> GetNames(IReadOnlyList<string> list, int max)
    {
        var result = new List<string>(Math.Min(max, list.Count));
        for (int i = 1; i <= max && i < list.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(list[i]))
                result.Add(list[i]);
        }
        return result;
    }

    /// <summary>
    /// Golpes que o Pokemon pode aprender oficialmente (nivel, TM/TR, tutor, ovo, encontro), como no PKHeX.
    /// Retorna um vetor indexado pelo ID do golpe.
    /// </summary>
    public static bool[] GetLearnableMoves(PKM pk)
    {
        var result = new bool[MoveNames.Count];
        try
        {
            var info = new LegalMoveInfo();
            info.ReloadMoves(new LegalityAnalysis(pk));
            for (int i = 1; i < result.Length && i <= pk.MaxMoveID; i++)
                result[i] = info.CanLearn((ushort)i);
        }
        catch
        {
            // sem analise: nenhum golpe destacado
        }
        return result;
    }

    /// <summary>Indice do nome na lista (sem diferenciar maiusculas), ou -1.</summary>
    public static int FindIndex(IReadOnlyList<string> list, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return -1;
        name = name.Trim();
        for (int i = 1; i < list.Count; i++)
        {
            if (string.Equals(list[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    public static SaveFile? LoadSave(string path)
    {
        // "arquivo.zip|entrada": save dentro de um zip (backups do JKSV)
        var sav = ZipSaves.IsZipPath(path, out _, out _) ? ZipSaves.Load(path) : SaveUtil.TryGetSaveFile(path, out var s) ? s : null;
        if (sav is not null)
            SaveNameHint.Apply(sav, path);
        return sav;
    }

    /// <summary>
    /// Torna este o save ativo para o PKHeX.Core, como o WinForms faz ao carregar um save: regras de legalidade que
    /// dependem do save (era do cartucho/console virtual da Gen 1-3, treinador ativo), modo dos sprites e listas
    /// filtradas (itens, bolas, golpes do jogo). Estado global: chamar sempre que o save ativo mudar (abrir, trocar de aba).
    /// Sem o <c>ParseSettings</c>, todo Pokemon de cartucho do Game Boy (Red/Blue/Yellow/Gold/Silver/Crystal) sai ilegal.
    /// </summary>
    public static void Activate(SaveFile sav)
    {
        ParseSettings.InitFromSaveFileData(sav);
        GameInfo.FilteredSources = new FilteredGameDataSource(sav, GameInfo.Sources);
        SpriteService.Activate(sav);
    }

    public static void ExportSave(SaveFile sav, string path)
    {
        if (ZipSaves.IsZipPath(path, out _, out _))
        {
            ZipSaves.Write(sav, path); // so a entrada do zip muda
            return;
        }
        var data = sav.Write();
        File.WriteAllBytes(path, data.Span);
    }

    public static string GetGameName(SaveFile sav) => GetPairName(sav.Version) ?? GameInfo.GetVersionName(sav.Version);

    /// <summary>Saves de par que o nome do arquivo nao separou (selo duplo): "Ruby / Sapphire" em vez de "RS".</summary>
    private static string? GetPairName(GameVersion version) => version switch
    {
        GameVersion.RB => "Red / Blue",
        GameVersion.GS => "Gold / Silver",
        GameVersion.RS => "Ruby / Sapphire",
        GameVersion.FRLG => "FireRed / LeafGreen",
        _ => null,
    };
    public static string GetVersionName(GameVersion version) => GameInfo.GetVersionName(version);

    /// <summary>Nome da caixa; saves sem nome gravado (ex.: recem-criados) mostram "Box N".</summary>
    public static string GetBoxName(SaveFile sav, int box)
        => sav is IBoxDetailNameRead n && n.GetBoxName(box) is { Length: > 0 } name && !string.IsNullOrWhiteSpace(name) ? name : $"Box {box + 1}";

    public static int GetBoxNameLength(SaveFile sav) => sav.Generation switch
    {
        2 when sav is SAV2 { Japanese: false, Korean: false } => 16,
        3 when sav is SAV3RSBox => 8 + SAV3RSBox.BoxNamePrefix,
        6 or 7 => 14,
        >= 8 => 16,
        _ => 8,
    };

    public static DateTime? GetAdventureStart(SaveFile sav)
    {
        try
        {
            if (sav is SAV8SWSH sw)
            {
                var card = sw.TrainerCard;
                return card.StartedYear >= 2000 ? new DateTime(card.StartedYear, card.StartedMonth, card.StartedDay) : null;
            }
            if (sav is SAV8LA la)
                return la.AdventureStart.Seconds > 0 ? la.AdventureStart.Timestamp : null;
            if (sav is SAV4 or SAV5 or SAV6 or SAV7 && sav.SecondsToStart > 0)
                return new DateTime(2000, 1, 1).AddSeconds(sav.SecondsToStart);
        }
        catch (ArgumentOutOfRangeException) { }
        return null;
    }

    public static string[] GetBoxWallpapers(SaveFile sav)
    {
        if (sav is not IBoxDetailWallpaper || sav is SAV8LA or SAV9ZA) return [];
        int count = sav.Generation switch { 3 or 7 => 16, 4 or 5 or 6 => 24, 8 when sav is SAV8BS => 32, 8 => 19, 9 => 20, _ => 0 };
        return Enumerable.Range(0, count).Select(i => sav.Generation <= 7 || sav is SAV8BS
            ? GameInfo.Strings.wallpapernames[i] : $"Papel de parede {i + 1}").ToArray();
    }

    public static bool IsEmpty(PKM pk) => pk.Species == 0;

    /// <summary>Pokemon em branco ja preenchido com os dados do treinador do save (como no PKHeX original).</summary>
    public static PKM CreateBlank(SaveFile sav)
    {
        var pk = sav.BlankPKM;
        EntityTemplates.TemplateFields(pk, sav);
        return pk;
    }

    public static PKM GetBoxSlot(SaveFile sav, int box, int slot) => sav.GetBoxSlotAtIndex(box, slot);

    public static void SetBoxSlot(SaveFile sav, PKM pk, int box, int slot)
    {
        pk.RefreshChecksum();
        sav.SetBoxSlotAtIndex(pk, box, slot);
    }

    public static int GetPartyCount(SaveFile sav) => sav.HasParty ? 6 : 0;
    public static PKM GetPartySlot(SaveFile sav, int slot) => slot < sav.PartyCount ? sav.GetPartySlotAtIndex(slot) : sav.BlankPKM;

    public static void SetPartySlot(SaveFile sav, PKM pk, int slot)
    {
        pk.RefreshChecksum();
        sav.SetPartySlotAtIndex(pk, Math.Min(slot, sav.PartyCount));
    }

    // Atributos e tipos
    /// <summary>Atributos finais na ordem da UI: PS, Atq, Def, AtE, DeE, Vel.</summary>
    public static int[] GetFinalStats(PKM pk)
    {
        var s = pk.GetStats(pk.PersonalInfo); // H/A/B/S/C/D
        return [s[0], s[1], s[2], s[4], s[5], s[3]];
    }

    /// <summary>Atributos base da especie, mesma ordem da UI.</summary>
    public static int[] GetBaseStats(PKM pk)
    {
        var p = pk.PersonalInfo;
        return [p.HP, p.ATK, p.DEF, p.SPA, p.SPD, p.SPE];
    }

    /// <summary>Modificador da natureza por atributo (UI order): +1, -1 ou 0.</summary>
    public static int[] GetNatureModifiers(PKM pk)
    {
        var result = new int[6];
        var n = (int)pk.StatAlignment;
        if (n >= 25 || n / 5 == n % 5)
            return result;
        // ordem interna da natureza: Atq, Def, Vel, AtE, DeE -> indices na UI
        ReadOnlySpan<int> map = [1, 2, 5, 3, 4];
        result[map[n / 5]] = 1;
        result[map[n % 5]] = -1;
        return result;
    }

    public static IReadOnlyList<(string Name, uint Argb)> GetTypes(PKM pk)
    {
        var (t1, t2) = GetSpeciesTypes(pk.PersonalInfo);
        var list = new List<(string, uint)> { TypeChip(t1) };
        if (t2 != t1)
            list.Add(TypeChip(t2));
        return list;

        static (string, uint) TypeChip(byte type)
        {
            var names = GameInfo.Strings.types;
            if (type >= names.Length)
                return ("?", 0xFF6B7280);
            return (names[type], (uint)Drawing.PokeSprite.TypeColor.GetTypeSpriteColor(type).ToArgb());
        }
    }

    /// <summary>Como os concursos funcionam para este Pokemon (regras do verificador do PKHeX).</summary>
    public enum ContestRule
    {
        /// <summary>Nenhum jogo da historia dele tem concursos: tudo precisa ser 0.</summary>
        None,
        /// <summary>Gen 3/4 e BD/SP: atributos vem de Pokeblocks/Poffins e o Sheen acompanha (faixa minima/maxima).</summary>
        Correlate,
        /// <summary>Omega Ruby/Alpha Sapphire: atributos livres, Sheen sempre 0.</summary>
        NoSheen,
        /// <summary>Passou por jogos com regras diferentes: qualquer Sheen.</summary>
        Free,
    }

    /// <summary>Regra de concursos e a faixa legal de Sheen para os atributos atuais.</summary>
    public static (ContestRule Rule, byte MinSheen, byte MaxSheen) GetContestRule(PKM pk)
    {
        if (pk is not IContestStats s)
            return (ContestRule.None, 0, 0);
        try
        {
            var info = new LegalityAnalysis(pk).Info;
            var h = info.EvoChainsAllGens;
            switch (ContestStatInfo.GetContestStatRestriction(pk, info.Generation, h))
            {
                case ContestStatGranting.None: return (ContestRule.None, 0, 0);
                case ContestStatGranting.NoSheen: return (ContestRule.NoSheen, 0, 0);
                case ContestStatGranting.Mixed: return (ContestRule.Free, 0, 255);
            }
            bool gen3 = info.Generation == 3;
            var method = gen3 ? ContestStatGrantingSheen.Gen3 : h.HasVisitedBDSP ? ContestStatGrantingSheen.Gen8b : ContestStatGrantingSheen.Gen4;
            var initial = ContestStatInfo.GetReferenceTemplate(info.EncounterMatch);
            var min = ContestStatInfo.CalculateMinimumSheen(s, initial, pk, method);
            var max = ContestStatInfo.CalculateMaximumSheen(s, pk.Nature, initial, gen3);
            return (ContestRule.Correlate, min, max);
        }
        catch
        {
            return (ContestRule.Free, 0, 255);
        }
    }

    /// <summary>
    /// Tipos da especie na numeracao moderna. Os dados pessoais da Gen 1 e 2 usam a numeracao interna do Game Boy
    /// (Inseto = 7, Fantasma = 8, Aco = 9, Fogo..Dragao = 20..26, Sombrio = 27); sem converter, o editor quebrava
    /// (indice fora da lista de nomes) ou mostrava o tipo errado (Gengar como Aco).
    /// </summary>
    public static (byte Type1, byte Type2) GetSpeciesTypes(IPersonalType p)
    {
        if (p is not (PersonalInfo1 or PersonalInfo2))
            return (p.Type1, p.Type2);
        return (FromGameBoyType(p.Type1), FromGameBoyType(p.Type2));
    }

    private static byte FromGameBoyType(byte type) => type switch
    {
        <= 5 => type,           // Normal, Lutador, Voador, Venenoso, Terrestre, Pedra
        7 => 6,                 // Inseto
        8 => 7,                 // Fantasma
        9 => 8,                 // Aco (Gen 2)
        >= 20 and <= 26 => (byte)(type - 11), // Fogo, Agua, Grama, Eletrico, Psiquico, Gelo, Dragao
        27 => 16,               // Sombrio (Gen 2)
        _ => type,
    };

    /// <summary>Tipo do golpe neste formato (nome e cor), ou null para "nenhum golpe".</summary>
    public static (string Name, uint Argb)? GetMoveType(ushort move, EntityContext context)
    {
        if (move == 0)
            return null;
        var type = MoveInfo.GetType(move, context);
        var names = GameInfo.Strings.types;
        var name = type < names.Length ? names[type] : "?";
        return (name, (uint)Drawing.PokeSprite.TypeColor.GetTypeSpriteColor(type).ToArgb());
    }

    // Golpes por indice (0-3): o PKM so expoe Move1..Move4 e seus PP.
    public static ushort GetMove(PKM pk, int i) => i switch { 0 => pk.Move1, 1 => pk.Move2, 2 => pk.Move3, _ => pk.Move4 };
    public static void SetMove(PKM pk, int i, ushort move)
    {
        switch (i) { case 0: pk.Move1 = move; break; case 1: pk.Move2 = move; break; case 2: pk.Move3 = move; break; default: pk.Move4 = move; break; }
    }
    public static int GetPP(PKM pk, int i) => i switch { 0 => pk.Move1_PP, 1 => pk.Move2_PP, 2 => pk.Move3_PP, _ => pk.Move4_PP };
    public static void SetPP(PKM pk, int i, int pp)
    {
        switch (i) { case 0: pk.Move1_PP = pp; break; case 1: pk.Move2_PP = pp; break; case 2: pk.Move3_PP = pp; break; default: pk.Move4_PP = pp; break; }
    }
    public static int GetPPUps(PKM pk, int i) => i switch { 0 => pk.Move1_PPUps, 1 => pk.Move2_PPUps, 2 => pk.Move3_PPUps, _ => pk.Move4_PPUps };
    public static void SetPPUps(PKM pk, int i, int ups)
    {
        switch (i) { case 0: pk.Move1_PPUps = ups; break; case 1: pk.Move2_PPUps = ups; break; case 2: pk.Move3_PPUps = ups; break; default: pk.Move4_PPUps = ups; break; }
    }
    /// <summary>PP maximo do golpe com os PP Ups atuais.</summary>
    public static int GetMaxPP(PKM pk, int i) => GetMove(pk, i) is var m and > 0 ? pk.GetMovePP(m, GetPPUps(pk, i)) : 0;

    public static string GetGenderSymbol(PKM pk) => pk.Gender switch { 0 => "♂", 1 => "♀", _ => "" };

    // Mover / trocar / copiar slots (usa as regras do Core: slots bloqueados, equipe so de ovos, etc.)
    public static ISlotInfo GetSlotInfo(SaveFile sav, int box, int slot)
        => box < 0 ? new SlotInfoParty(slot) : new SlotInfoBox(box, slot, sav);

    private static PKM Read(SaveFile sav, ISlotInfo s)
        => s is SlotInfoParty p ? GetPartySlot(sav, p.Slot) : s.Read(sav);

    /// <summary>Move (troca) ou copia o Pokemon de <paramref name="src"/> para <paramref name="dst"/>. Retorna erro ou null.</summary>
    /// <param name="overwrite">Mover sobrescrevendo: o destino recebe o Pokemon e a origem fica vazia (sem troca).</param>
    public static string? MoveSlot(SaveFile sav, ISlotInfo src, ISlotInfo dst, bool copy, bool overwrite = false)
    {
        if (src == dst)
            return "";
        if (!src.CanWriteTo(sav) || !dst.CanWriteTo(sav))
            return "Slot bloqueado pelo jogo.";

        var a = Read(sav, src);
        var b = Read(sav, dst);
        if (IsEmpty(a))
            return "";

        if (dst.CanWriteTo(sav, a) != WriteBlockedMessage.None)
            return "A equipe não pode ficar só com ovos.";

        if (copy)
        {
            var clone = a.Clone();
            clone.RefreshChecksum();
            dst.WriteTo(sav, clone);
            return null;
        }

        if (overwrite)
        {
            // Origem fica vazia: na equipe, ela nao pode ficar sem Pokemon (que nao seja ovo).
            if (src is SlotInfoParty sp0 && dst is not SlotInfoParty && sav.IsPartyAllEggs(sp0.Slot))
                return "A equipe precisa ter pelo menos um Pokémon (que não seja ovo).";
            dst.WriteTo(sav, a);
            if (src is SlotInfoParty && dst is SlotInfoParty dp && ((SlotInfoParty)src).Slot < dp.Slot)
                new SlotInfoParty(((SlotInfoParty)src).Slot).WriteTo(sav, sav.BlankPKM); // equipe: os seguintes sobem
            else
                src.WriteTo(sav, sav.BlankPKM);
            return null;
        }

        // Retirar da equipe: ela nao pode ficar vazia nem so com ovos.
        if (src is SlotInfoParty sp && dst is not SlotInfoParty && (IsEmpty(b) || b.IsEgg) && sav.IsPartyAllEggs(sp.Slot))
            return "A equipe precisa ter pelo menos um Pokémon (que não seja ovo).";
        if (!IsEmpty(b) && src.CanWriteTo(sav, b) != WriteBlockedMessage.None)
            return "A equipe não pode ficar só com ovos.";

        if (src is SlotInfoParty && dst is SlotInfoParty && IsEmpty(b))
        {
            // Reordenar para o fim da equipe: remove e acrescenta.
            src.WriteTo(sav, sav.BlankPKM);
            new SlotInfoParty(sav.PartyCount).WriteTo(sav, a);
            return null;
        }

        dst.WriteTo(sav, a);
        src.WriteTo(sav, IsEmpty(b) ? sav.BlankPKM : b);
        return null;
    }

    /// <summary>O slot de caixa pode ser alterado (o jogo bloqueia alguns, ex.: times de batalha).</summary>
    public static bool CanWriteBoxSlot(SaveFile sav, int box, int slot) => new SlotInfoBox(box, slot, sav).CanWriteTo(sav);

    /// <summary>Apaga o Pokemon do slot (na equipe, os seguintes sobem uma posicao). Retorna erro ou null.</summary>
    public static string? DeleteSlot(SaveFile sav, ISlotInfo slot)
    {
        if (!slot.CanWriteTo(sav))
            return "Slot bloqueado pelo jogo.";
        if (IsEmpty(Read(sav, slot)))
            return "";
        if (slot is SlotInfoParty p && sav.IsPartyAllEggs(p.Slot))
            return "A equipe precisa ter pelo menos um Pokémon (que não seja ovo).";
        slot.WriteTo(sav, sav.BlankPKM);
        return null;
    }

    // Ordenar caixas (usa os ordenadores do Core; vazios e ovos vao para o fim, slots bloqueados ficam onde estao)
    public sealed record BoxSortOption(string Name, Func<IEnumerable<PKM>, IEnumerable<PKM>> Sorter);

    /// <summary>Criterios de ordenacao que se aplicam ao save (ex.: data de captura so a partir da Gen 4). Sem save = bank (todos).</summary>
    public static IReadOnlyList<BoxSortOption> GetBoxSortOptions(SaveFile? sav)
    {
        var list = new List<BoxSortOption>
        {
            new("Nº da Pokédex", p => p.OrderBySpecies()),
            new("Nº da Pokédex (decrescente)", p => p.OrderByDescendingSpecies()),
            new("Nome da espécie (A–Z)", p => p.OrderBySpeciesName(GameInfo.Strings.Species)),
            new("Nível (maior primeiro)", p => p.OrderByDescendingLevel()),
            new("Nível (menor primeiro)", p => p.OrderByLevel()),
            new("Shiny primeiro", p => p.OrderByCustom(pk => !pk.IsShiny)),
            new("Tipo", p => p.OrderByCustom(pk => GetSpeciesTypes(pk.PersonalInfo).Type1, pk => GetSpeciesTypes(pk.PersonalInfo).Type2)),
            new("IVs (maior total primeiro)", p => p.OrderByCustom(pk => -pk.IVTotal)),
        };
        if (sav is null || sav.Generation >= 4)
            list.Add(new("Data de captura", p => p.OrderByDateObtained()));
        list.Add(new("Juntar (tirar espaços vazios)", p => p.OrderBy(pk => pk.Species == 0)));
        return list;
    }

    /// <summary>Ordena as caixas <paramref name="first"/>..<paramref name="last"/> do save. Retorna quantos Pokemon foram reposicionados.</summary>
    public static int SortBoxes(SaveFile sav, BoxSortOption option, int first, int last)
        => sav.SortBoxes(first, last, (p, _) => option.Sorter(p));

    /// <summary>Ordena uma lista de Pokemon (vazios ficam de fora) com o criterio escolhido.</summary>
    public static IReadOnlyList<PKM> Sort(IEnumerable<PKM> list, BoxSortOption option)
        => [.. option.Sorter(list.Where(pk => !IsEmpty(pk)))];

    /// <summary>Carrega um arquivo .pk* e converte para o formato do save. Retorna null se nao for compativel.</summary>
    public static PKM? LoadEntityFile(SaveFile sav, string path)
    {
        var file = FileUtil.GetSupportedFile(path, sav);
        if (file is MysteryGift { IsEntity: true } gift)
            return EncounterDatabase.ToEntity(sav, gift, out _);
        if (file is not PKM pk)
            return null;
        if (pk.GetType() == sav.PKMType)
            return pk;
        return EntityConverter.ConvertToType(pk, sav.PKMType, out _);
    }

    /// <summary>
    /// Converte um Pokemon (de qualquer geracao) para o formato do save, como o PKHeX faz ao importar.
    /// Retorna null e o motivo se nao der (ex.: especie que nao existe naquele jogo).
    /// </summary>
    public static PKM? ConvertForSave(SaveFile sav, PKM pk, out string? error)
    {
        error = null;
        try
        {
            var clone = pk.Clone();
            if (clone.GetType() == sav.PKMType)
                return clone;
            var converted = EntityConverter.ConvertToType(clone, sav.PKMType, out var result);
            if (converted is null)
            {
                error = string.Join(" ", result.GetDisplayString(clone, sav.PKMType).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                return null;
            }
            if (converted.Species > sav.MaxSpeciesID || converted.Species == 0)
            {
                error = $"{SpeciesNames[pk.Species]} não existe em {GetGameName(sav)}.";
                return null;
            }
            sav.AdaptToSaveFile(converted);
            converted.RefreshChecksum();
            return converted;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Grava um Pokemon vindo de arquivo num slot. Retorna erro ou null.</summary>
    public static string? ImportToSlot(SaveFile sav, ISlotInfo dst, PKM pk)
    {
        if (!dst.CanWriteTo(sav))
            return "Slot bloqueado pelo jogo.";
        pk.RefreshChecksum();
        dst.WriteTo(sav, pk);
        return null;
    }

    /// <summary>Nome de arquivo sugerido para exportar o Pokemon (ex.: "025 - Pikachu - 1A2B3C4D.pk3").</summary>
    public static string GetEntityFileName(PKM pk) => PathUtil.CleanFileName(pk.FileName);

    /// <summary>Grava o Pokemon num arquivo .pk* (dados decifrados, como o PKHeX original).</summary>
    public static void ExportEntity(PKM pk, string path)
    {
        pk.RefreshChecksum();
        var data = new byte[pk.SIZE_PARTY];
        pk.WriteDecryptedDataParty(data);
        File.WriteAllBytes(path, data);
    }

    /// <summary>Extensoes .pk* aceitas pelo save (para o seletor de arquivos).</summary>
    public static IReadOnlyList<string> GetEntityExtensions(SaveFile sav) => sav.PKMExtensions;

    /// <summary>Legalidade rapida para o icone do slot: true/false, ou null se a analise falhar.</summary>
    public static bool? IsLegal(PKM pk)
    {
        try { return new LegalityAnalysis(pk).Valid; }
        catch { return null; }
    }

    // Ordem do resumo do hover: a mesma do PKHeX (primeira linha + BattleTemplateConfig.DefaultHover).
    private static readonly BattleTemplateToken[] HoverOrder = [BattleTemplateToken.FirstLine, .. BattleTemplateConfig.DefaultHover];

    /// <summary>
    /// Legalidade e o resumo que o PKHeX mostra ao passar o mouse no slot: set (item, habilidade, nivel, IVs/EVs,
    /// natureza, golpes) + informacoes do encontro (tipo, local, PID, Origin Seed...). Uma unica analise para os dois.
    /// </summary>
    public static (bool? Legal, string Summary) AnalyzeSlot(PKM pk)
    {
        LegalityAnalysis la;
        try { la = new LegalityAnalysis(pk); }
        catch (Exception ex) { return (null, ex.Message); }

        var lines = new List<string>(16);
        try
        {
            var settings = new BattleTemplateExportSettings(HoverOrder, GameInfo.CurrentLanguage);
            lines.Add(ShowdownParsing.GetLocalizedPreviewText(pk, settings));
        }
        catch
        {
            lines.Add(SpeciesNames[pk.Species]);
        }
        try
        {
            var ctx = LegalityLocalizationContext.Create(la, GameInfo.CurrentLanguage);
            lines.Add(string.Empty);
            LegalityFormatting.AddEncounterInfo(ctx, lines);
            if (!la.Valid && GetLegalityIssues(pk, 1) is [var issue])
            {
                lines.Add(string.Empty);
                lines.Add("⚠ " + issue);
            }
        }
        catch
        {
            // sem informacoes de encontro: fica so o set
        }
        return (la.Valid, string.Join(Environment.NewLine, lines).Trim());
    }

    // Showdown
    public static string ToShowdown(PKM pk) => ShowdownParsing.GetShowdownText(pk);

    /// <summary>Aplica um set Showdown ao Pokemon. Retorna mensagem de erro ou null.</summary>
    public static string? ApplyShowdown(PKM pk, string text)
    {
        if (!ShowdownParsing.TryParseAnyLanguage(text, out var set) || set.Species == 0)
            return "Texto Showdown inválido.";
        pk.ApplySetDetails(set);
        return set.InvalidLines.Count == 0 ? null : $"{set.InvalidLines.Count} linha(s) ignorada(s).";
    }

    // Mochila
    public static PlayerBag GetBag(SaveFile sav) => sav.Inventory;
    public static void SaveBag(SaveFile sav, PlayerBag bag) => bag.CopyTo(sav);
    public static bool IsBagItemIdEditable(SaveFile sav) => sav is not (SAV9ZA or SAV9SV);

    /// <summary>
    /// Troca a especie como o PKHeX original: forma 0, apelido padrao (se nao tinha apelido),
    /// habilidade no mesmo slot e genero valido para a nova especie.
    /// </summary>
    // Marcacoes (●▲■♥★◆). Gen 3 tem 4 (ordem ●■▲♥); Gen 4-6, 6 liga/desliga; Gen 7+, 6 com cor (azul/rosa).
    public static int GetMarkingCount(PKM pk) => pk is IAppliedMarkings m ? m.MarkingCount : 0;

    public static string GetMarkingSymbol(PKM pk, int index) => pk is IAppliedMarkings3 and not IAppliedMarkings4
        ? (index switch { 0 => "●", 1 => "■", 2 => "▲", _ => "♥" })
        : (index switch { 0 => "●", 1 => "▲", 2 => "■", 3 => "♥", 4 => "★", _ => "◆" });

    /// <summary>0 = desligada, 1 = ligada (azul), 2 = rosa.</summary>
    public static int GetMarking(PKM pk, int index) => pk switch
    {
        IAppliedMarkings<bool> b => b.GetMarking(index) ? 1 : 0,
        IAppliedMarkings<MarkingColor> c => c.GetMarking(index) switch { MarkingColor.Blue => 1, MarkingColor.Pink => 2, _ => 0 },
        _ => 0,
    };

    public static void CycleMarking(PKM pk, int index)
    {
        switch (pk)
        {
            case IAppliedMarkings<bool> b:
                b.SetMarking(index, !b.GetMarking(index));
                break;
            case IAppliedMarkings<MarkingColor> c:
                c.SetMarking(index, c.GetMarking(index) switch { MarkingColor.None => MarkingColor.Blue, MarkingColor.Blue => MarkingColor.Pink, _ => MarkingColor.None });
                break;
        }
    }

    // Hyper Training: indice na ordem da interface (PS, Atq, Def, AtE, DeE, Vel)
    public static bool GetHyperTrain(IHyperTrain h, int index) => index switch
    {
        0 => h.HT_HP, 1 => h.HT_ATK, 2 => h.HT_DEF, 3 => h.HT_SPA, 4 => h.HT_SPD, _ => h.HT_SPE,
    };

    public static void SetHyperTrain(IHyperTrain h, int index, bool value)
    {
        switch (index)
        {
            case 0: h.HT_HP = value; break;
            case 1: h.HT_ATK = value; break;
            case 2: h.HT_DEF = value; break;
            case 3: h.HT_SPA = value; break;
            case 4: h.HT_SPD = value; break;
            default: h.HT_SPE = value; break;
        }
        if (h is PKM pk)
            pk.ResetPartyStats();
    }

    // Fitas
    /// <summary>Uma fita do formato do Pokemon: propriedade, nome traduzido, valor (0/1 ou contagem) e maximo.</summary>
    public sealed record RibbonEntry(string Property, string Name, bool IsCount, int Value, int Max);

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "O app nao e publicado com trimming.")]
    public static IReadOnlyList<RibbonEntry> GetRibbons(PKM pk)
    {
        var list = new List<RibbonEntry>();
        foreach (var r in RibbonInfo.GetRibbonInfo(pk))
        {
            var name = GameInfo.Strings.Ribbons.GetNameSafe(r.Name, out var n) ? n : r.Name.Replace(RibbonInfo.PropertyPrefix, "");
            list.Add(r.Type == RibbonValueType.Boolean
                ? new RibbonEntry(r.Name, name, false, r.HasRibbon ? 1 : 0, 1)
                : new RibbonEntry(r.Name, name, true, r.RibbonCount, r.MaxCount));
        }
        return list;
    }

    public static void SetRibbon(PKM pk, RibbonEntry ribbon, int value)
    {
        object v = ribbon.IsCount ? (byte)Math.Clamp(value, 0, ribbon.Max) : value > 0;
        ReflectUtil.SetValue(pk, ribbon.Property, v);
    }

    /// <summary>Todas as fitas que o Pokemon pode ter legalmente (pela historia dele).</summary>
    public static void SetAllValidRibbons(PKM pk) => RibbonApplicator.SetAllValidRibbons(pk);
    public static void RemoveAllRibbons(PKM pk) => RibbonApplicator.RemoveAllValidRibbons(pk);

    // Memorias (Gen 6+)
    private static MemoryStrings? _memoryStrings;
    public static MemoryStrings MemoryTexts => _memoryStrings ??= new MemoryStrings(GameInfo.Strings);

    /// <summary>Geracao das regras de memoria: 8 a partir de Sword/Shield, senao 6.</summary>
    public static int GetMemoryGen(PKM pk, bool originalTrainer)
    {
        var gen = originalTrainer && pk.Generation > 0 ? pk.Generation : pk.Format;
        return gen >= 8 ? 8 : 6;
    }

    public static IReadOnlyList<ComboItem> GetMemoryArguments(byte memory, int memoryGen)
        => MemoryTexts.GetArgumentStrings(Memories.GetMemoryArgType(memory, memoryGen), memoryGen);

    /// <summary>A mudanca deixa o Pokemon legal? Testa numa copia (filtros do modo legal).</summary>
    public static bool IsLegalWith(PKM pk, Action<PKM> change)
    {
        try
        {
            var copy = pk.Clone();
            change(copy);
            return new LegalityAnalysis(copy).Valid;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Habilidades da especie/forma: valor = indice (0, 1 ou 2 = oculta).</summary>
    public static IReadOnlyList<ComboItem> GetAbilityOptions(PKM pk)
    {
        var p = pk.PersonalInfo;
        var current = GetAbilityIndex(pk);
        var list = new List<ComboItem>();
        var byId = new Dictionary<int, int>(); // habilidade -> posicao na lista (slots repetidos viram uma opcao)
        for (int i = 0; i < p.AbilityCount; i++)
        {
            var id = p.GetAbilityAtIndex(i);
            if (id <= 0 || id >= AbilityNames.Count)
                continue;
            var item = new ComboItem(i == 2 ? $"{AbilityNames[id]} (oculta)" : AbilityNames[id], i);
            if (byId.TryGetValue(id, out var at))
            {
                if (i == current)
                    list[at] = item; // mesmo nome nos dois slots: fica o slot que o Pokemon usa
                continue;
            }
            byId[id] = list.Count;
            list.Add(item);
        }
        return list;
    }

    /// <summary>Indice da habilidade atual (0, 1 ou 2), pelo AbilityNumber (1, 2, 4).</summary>
    public static int GetAbilityIndex(PKM pk) => pk.AbilityNumber switch { 2 => 1, 4 => 2, _ => 0 };

    public static void SetAbilityIndex(PKM pk, int index) => pk.SetAbilityIndex(index);

    /// <summary>Formas da especie que existem no jogo do Pokemon (valor = forma). Vazio se a especie so tem uma.</summary>
    /// <param name="table">Tabela do jogo do save (formas presentes neste jogo).</param>
    public static IReadOnlyList<ComboItem> GetFormOptions(PKM pk, IPersonalTable table, bool hideBattleOnly)
    {
        if (pk.Species > table.MaxSpeciesID)
            return [];
        // A lista de nomes do Core inclui formas que a tabela de atributos nao separa (ex.: as letras do Unown).
        string[] names;
        try { names = FormConverter.GetFormList(pk.Species, GameInfo.Strings.types, GameInfo.Strings.forms, GameInfo.GenderSymbolUnicode, pk.Context); }
        catch { names = []; }
        var tableCount = table.GetFormEntry(pk.Species, 0).FormCount;
        var count = Math.Max(names.Length, (int)tableCount);
        if (count <= 1)
            return [];
        var list = new List<ComboItem>();
        for (byte f = 0; f < count; f++)
        {
            if (f < tableCount && !table.IsPresentInGame(pk.Species, f))
                continue;
            if (hideBattleOnly && f != pk.Form && FormInfo.IsBattleOnlyForm(pk.Species, f, pk.Format))
                continue;
            var name = f < names.Length ? names[f] : "";
            list.Add(new ComboItem(string.IsNullOrWhiteSpace(name) ? $"Forma {f}" : name, f));
        }
        return list.Count > 1 ? list : [];
    }

    /// <summary>Especies cuja forma e o genero (forma 0 = macho, 1 = femea): Meowstic, Indeedee, Basculegion, Oinkologne.</summary>
    public static bool IsGenderForm(ushort species) => species is
        (ushort)PKHeX.Core.Species.Meowstic or (ushort)PKHeX.Core.Species.Indeedee
        or (ushort)PKHeX.Core.Species.Basculegion or (ushort)PKHeX.Core.Species.Oinkologne;

    /// <summary>
    /// Troca o genero como o jogo permitiria: nas especies em que a forma e o genero, troca a forma junto; da Gen 3 a
    /// 5 o genero vem do PID (gera outro PID com a mesma natureza); da Gen 6 em diante e um campo proprio.
    /// </summary>
    public static void SetGender(PKM pk, byte gender)
    {
        if (IsGenderForm(pk.Species) && pk.Format >= 6)
        {
            SetForm(pk, gender); // SetForm ajusta o genero pela forma
            pk.Gender = gender;
            return;
        }
        if (pk.Gen3 || pk.Gen4 || pk.Gen5)
            pk.SetPIDGender(gender);
        pk.Gender = gender;
        pk.RefreshChecksum();
    }

    /// <summary>Troca a forma mantendo o slot da habilidade e o genero coerente.</summary>
    public static void SetForm(PKM pk, byte form)
    {
        if (pk.Form == form)
            return;
        var slot = GetAbilityIndex(pk);
        pk.Form = form;
        pk.RefreshAbility(slot);
        pk.Gender = pk.GetSaneGender();
    }

    /// <summary>Tera Types (18 tipos + Stellar), valor = numero do tipo.</summary>
    public static IReadOnlyList<ComboItem> GetTeraOptions()
    {
        var names = GameInfo.Strings.types;
        var list = new List<ComboItem>();
        for (int t = 0; t <= TeraTypeUtil.MaxType && t < names.Length; t++)
            list.Add(new ComboItem(names[t], t));
        list.Add(new ComboItem("Stellar", TeraTypeUtil.Stellar));
        return list;
    }

    /// <summary>Tera Type atual (o trocado com Tera Shards, se houver; senao o original).</summary>
    public static int GetTeraType(PKM pk) => pk is ITeraType t ? (int)t.GetTeraType() : -1;

    /// <summary>Troca o Tera Type como as Tera Shards: guarda no "override" (voltar ao original limpa o override).</summary>
    public static void SetTeraType(PKM pk, int type)
    {
        if (pk is not ITeraType t)
            return;
        t.TeraTypeOverride = type == (int)t.TeraTypeOriginal ? (MoveType)TeraTypeUtil.OverrideNone : (MoveType)type;
    }

    /// <summary>O encontro reconhecido nunca e shiny (ex.: lendarios com shiny lock).</summary>
    public static bool IsShinyLocked(PKM pk)
    {
        try { return new LegalityAnalysis(pk).EncounterMatch.Shiny == Shiny.Never; }
        catch { return false; }
    }

    /// <summary>Descricao do encontro reconhecido pela analise de legalidade.</summary>
    public static string GetCurrentEncounterLabel(PKM pk)
    {
        try
        {
            var enc = new LegalityAnalysis(pk).EncounterMatch;
            return enc is IEncounterInfo info && enc is not EncounterInvalid ? EncounterDatabase.GetShortLabel(info) : "não reconhecido";
        }
        catch
        {
            return "não reconhecido";
        }
    }

    public static void ChangeSpecies(PKM pk, ushort species)
    {
        if (pk.Species == species)
            return;
        bool nicknamed = pk.IsNicknamed;
        int abilitySlot = pk.AbilityNumber switch { 2 => 1, 4 => 2, _ => 0 };
        pk.Species = species;
        pk.Form = 0;
        if (!nicknamed)
            pk.ClearNickname();
        pk.RefreshAbility(abilitySlot);
        pk.Gender = pk.GetSaneGender();
    }

    // Evoluir por troca
    /// <summary>Uma evolucao por troca possivel: destino, o que a troca exige e se algo impede (Everstone).</summary>
    public sealed record TradeEvolution(ushort Species, byte Form, string Name, string Requirement, int ItemId, string? Blocked);

    public sealed record FriendshipEvolution(ushort Species, byte Form, string Name, string Requirement, EvolutionType Method, string? Blocked);

    public static IReadOnlyList<FriendshipEvolution> GetFriendshipEvolutions(PKM pk, SaveFile? sav)
    {
        if (IsEmpty(pk) || pk.IsEgg || pk.Format < 2) return [];
        var list = new List<FriendshipEvolution>();
        var methods = EvolutionTree.GetEvolutionTree(pk.Context).Forward.GetForward(pk.Species, pk.Form).ToArray();
        // A tabela da Gen 2 simplifica felicidade para LevelUp; recupera o requisito das espécies do jogo.
        if (pk.Context == EntityContext.Gen2)
            methods = [.. methods.Select(m => m with { Method = m.Species switch
            {
                (ushort)Species.Espeon => EvolutionType.LevelUpFriendshipMorning,
                (ushort)Species.Umbreon => EvolutionType.LevelUpFriendshipNight,
                (ushort)Species.Crobat or (ushort)Species.Blissey or (ushort)Species.Togetic
                    or (ushort)Species.Pikachu or (ushort)Species.Clefairy or (ushort)Species.Jigglypuff => EvolutionType.LevelUpFriendship,
                _ => m.Method,
            } })];
        bool fairyMove = Enumerable.Range(0, 4).Any(i => GetMove(pk, i) != 0 && MoveInfo.GetType(GetMove(pk, i), pk.Context) == 17);
        int threshold = pk.Format >= 8 ? 160 : 220;
        bool sylveonReady = methods.Any(m => m.Species == (ushort)Species.Sylveon) && fairyMove && (pk.Format >= 8 || pk is IAffection);
        foreach (var m in methods)
        {
            if (m.Method is not (EvolutionType.LevelUpFriendship or EvolutionType.LevelUpFriendshipMorning or EvolutionType.LevelUpFriendshipNight or EvolutionType.LevelUpAffection50MoveType)) continue;
            byte form = m.GetDestinationForm(pk.Form);
            if (m.Species > pk.MaxSpeciesID || sav is not null && !sav.Personal.IsPresentInGame(m.Species, form)) continue;
            bool affection = m.Method == EvolutionType.LevelUpAffection50MoveType && pk.Format < 8;
            string time = m.Method switch { EvolutionType.LevelUpFriendshipMorning => " de dia", EvolutionType.LevelUpFriendshipNight => " à noite", _ => "" };
            bool manual = pk.Context is EntityContext.Gen8a or EntityContext.Gen9a;
            string requirement = (manual ? "evoluir" : pk.CurrentLevel == 100 && pk.Format >= 8 ? "usar Rare Candy no nível 100" : "subir 1 nível") + time
                + (affection ? ", carinho ≥ 50 (2 corações)" : $", felicidade ≥ {threshold}")
                + (m.Method == EvolutionType.LevelUpAffection50MoveType ? ", sabendo golpe Fairy" : "")
                + (GetHeldItemName(pk) == "Everstone" ? ", tira a Everstone" : "");
            // Felicidade, carinho e Everstone o botao resolve; o resto precisa de acao do usuario.
            string? blocked = !manual && pk.CurrentLevel == 100 && pk.Format < 8 ? "precisa subir de nível e já está no nível 100"
                : m.Method == EvolutionType.LevelUpAffection50MoveType && !fairyMove ? "precisa saber um golpe Fairy"
                : m.Species is (ushort)Species.Espeon or (ushort)Species.Umbreon && sylveonReady ? "Sylveon tem prioridade; remova o golpe Fairy para escolher esta evolução"
                : sav?.Version is GameVersion.FR or GameVersion.LG && m.Method is EvolutionType.LevelUpFriendshipMorning or EvolutionType.LevelUpFriendshipNight ? "FireRed/LeafGreen não têm ciclo de dia/noite para esta evolução"
                : null;
            list.Add(new(m.Species, form, SpeciesNames[m.Species], requirement, m.Method, blocked));
        }
        return list;
    }

    public static string EvolveByFriendship(PKM pk, FriendshipEvolution evo, SaveFile? sav)
    {
        var current = GetFriendshipEvolutions(pk, sav).FirstOrDefault(e => e.Species == evo.Species && e.Form == evo.Form && e.Method == evo.Method);
        if (current is null || current.Blocked is not null) throw new InvalidOperationException(current?.Blocked ?? "Esta evolução não está disponível.");
        RemoveEverstone(pk);
        if (evo.Method != EvolutionType.LevelUpAffection50MoveType || pk.Format >= 8)
            pk.CurrentFriendship = (byte)Math.Max(pk.CurrentFriendship, pk.Format >= 8 ? 160 : 220);
        else if (pk is IAffection a)
        {
            if (pk.CurrentHandler == 0) a.OriginalTrainerAffection = Math.Max(a.OriginalTrainerAffection, (byte)50);
            else a.HandlingTrainerAffection = Math.Max(a.HandlingTrainerAffection, (byte)50);
        }
        if (pk.Context is not (EntityContext.Gen8a or EntityContext.Gen9a) && pk.CurrentLevel < 100) pk.CurrentLevel++;
        return EvolveByItem(pk, new ItemEvolution(evo.Species, evo.Form, evo.Name, current.Requirement, 0, null));
    }

    /// <summary>Evolucoes por troca da especie/forma atual, pela tabela de evolucoes do jogo do Pokemon.</summary>
    public static IReadOnlyList<TradeEvolution> GetTradeEvolutions(PKM pk)
    {
        if (IsEmpty(pk) || pk.IsEgg)
            return [];
        EvolutionTree tree;
        try { tree = EvolutionTree.GetEvolutionTree(pk.Context); }
        catch (ArgumentOutOfRangeException) { return []; }
        var items = GetItemNames(pk);
        var held = GetHeldItemName(pk);
        var list = new List<TradeEvolution>();
        foreach (var m in tree.Forward.GetForward(pk.Species, pk.Form).Span)
        {
            if (!m.Method.IsTrade || m.Species > pk.MaxSpeciesID)
                continue;
            int item = m.Method == EvolutionType.TradeHeldItem ? m.Argument
                : pk.Context == EntityContext.Gen2 && Gen2TradeItems.TryGetValue(m.Species, out var name2) ? FindItem(items, name2) : 0;
            var requirement = m.Method == EvolutionType.TradeShelmetKarrablast
                ? $"troca por um {SpeciesNames[pk.Species == (ushort)PKHeX.Core.Species.Shelmet ? (ushort)PKHeX.Core.Species.Karrablast : (ushort)PKHeX.Core.Species.Shelmet]}"
                : item > 0 ? $"troca segurando {(item < items.Count && items[item].Length > 0 ? items[item] : $"item #{item}")}"
                : "troca";
            var blocked = held == "Everstone" ? "está segurando uma Everstone, que impede a evolução" : null;
            list.Add(new TradeEvolution(m.Species, m.GetDestinationForm(pk.Form), SpeciesNames[m.Species], requirement, item, blocked));
        }
        return list;
    }

    /// <summary>Na Gen 2 a tabela do Core nao guarda o item exigido; estes sao os do jogo (destino → item).</summary>
    private static readonly Dictionary<ushort, string> Gen2TradeItems = new()
    {
        [(ushort)PKHeX.Core.Species.Steelix] = "Metal Coat", [(ushort)PKHeX.Core.Species.Scizor] = "Metal Coat",
        [(ushort)PKHeX.Core.Species.Kingdra] = "Dragon Scale", [(ushort)PKHeX.Core.Species.Porygon2] = "Up-Grade",
        [(ushort)PKHeX.Core.Species.Politoed] = "King's Rock", [(ushort)PKHeX.Core.Species.Slowking] = "King's Rock",
    };

    /// <summary>Indice do item pelo nome, ignorando maiusculas, espacos, hifens e apostrofos (os nomes variam entre geracoes).</summary>
    private static int FindItem(IReadOnlyList<string> items, string name)
    {
        static string Key(string s) => new([.. s.ToUpperInvariant().Where(char.IsLetterOrDigit)]);
        var key = Key(name);
        for (int i = 1; i < items.Count; i++)
            if (Key(items[i]) == key)
                return i;
        return 0;
    }

    /// <summary>
    /// Evolui como numa troca de verdade: o Pokemon vai para outro treinador, evolui (o item exigido e consumido) e volta.
    /// A partir da Gen 6 o jogo registra quem o recebeu (HT); sem isso a evolucao por troca fica ilegal, entao, se ele
    /// nunca saiu do dono, fica registrado um parceiro de troca generico ("PKHeX"). Retorna o texto do que foi feito.
    /// </summary>
    /// <summary>Evolucao por item (pedras e afins): destino, item usado e o motivo se nao der agora.</summary>
    public sealed record ItemEvolution(ushort Species, byte Form, string Name, string Requirement, int ItemId, string? Blocked);

    public static IReadOnlyList<ItemEvolution> GetBeautyEvolutions(PKM pk, SaveFile? sav)
    {
        // Z-A conserva os atributos de concurso, mas não oferece evolução por Beauty.
        if (IsEmpty(pk) || pk.IsEgg || pk.Context == EntityContext.Gen9a || pk is not IContestStatsReadOnly stats) return [];
        var list = new List<ItemEvolution>();
        foreach (var m in EvolutionTree.GetEvolutionTree(pk.Context).Forward.GetForward(pk.Species, pk.Form).Span)
        {
            byte form = m.GetDestinationForm(pk.Form);
            if (m.Method != EvolutionType.LevelUpBeauty || m.Species > pk.MaxSpeciesID
                || sav is not null && !sav.Personal.IsPresentInGame(m.Species, form)) continue;
            // O botao cumpre os requisitos: sobe o Beauty (e o Sheen junto, onde ele acompanha) e tira a Everstone.
            // So bloqueia o que nao da para resolver: nivel 100 antes da Gen 8 ou um historico sem concursos.
            bool raise = stats.ContestBeauty < m.Argument;
            string? blocked = pk.CurrentLevel == 100 && pk.Format < 8 ? "precisa subir de nível e já está no nível 100"
                : raise && GetContestRule(pk).Rule == ContestRule.None ? "o Beauty não pode subir nos jogos por onde ele passou (sem concursos); use a troca com Prism Scale, se houver" : null;
            string requirement = (pk.CurrentLevel == 100 ? "usar Rare Candy no nível 100" : "subir 1 nível")
                + (raise ? $", Beauty sobe de {stats.ContestBeauty} para {m.Argument}" : $", Beauty ≥ {m.Argument}")
                + (GetHeldItemName(pk) == "Everstone" ? ", tira a Everstone" : "");
            list.Add(new(m.Species, form, SpeciesNames[m.Species], requirement, 0, blocked));
        }
        return list;
    }

    public static string EvolveByBeauty(PKM pk, ItemEvolution evo, SaveFile? sav)
    {
        var current = GetBeautyEvolutions(pk, sav).FirstOrDefault(e => e.Species == evo.Species && e.Form == evo.Form);
        if (current is null || current.Blocked is not null) throw new InvalidOperationException(current?.Blocked ?? "Esta evolução não está disponível.");
        RemoveEverstone(pk);
        if (pk is IContestStats contest && contest.ContestBeauty < BeautyNeeded(pk, evo))
        {
            contest.ContestBeauty = BeautyNeeded(pk, evo);
            // Gen 3/4 e BD/SP: Pokeblocks/Poffins dao Sheen junto; ajusta para a faixa legal dos novos atributos.
            var (rule, min, max) = GetContestRule(pk);
            if (rule == ContestRule.Correlate)
                contest.ContestSheen = Math.Clamp(contest.ContestSheen, min, Math.Max(min, max));
            else if (rule == ContestRule.NoSheen)
                contest.ContestSheen = 0;
        }
        if (pk.CurrentLevel < 100) pk.CurrentLevel++;
        return EvolveByItem(pk, current);
    }

    private static byte BeautyNeeded(PKM pk, ItemEvolution evo)
    {
        foreach (var m in EvolutionTree.GetEvolutionTree(pk.Context).Forward.GetForward(pk.Species, pk.Form).Span)
            if (m.Method == EvolutionType.LevelUpBeauty && m.Species == evo.Species)
                return (byte)Math.Min(255, (int)m.Argument);
        return 0;
    }

    /// <summary>Tira a Everstone segurada (ela impede a evolucao).</summary>
    private static void RemoveEverstone(PKM pk)
    {
        if (GetHeldItemName(pk) == "Everstone")
            pk.HeldItem = 0;
    }

    /// <summary>Evolucoes por item da especie atual (ex.: Pikachu + Thunder Stone → Raichu, Nidorino + Moon Stone → Nidoking).</summary>
    public static IReadOnlyList<ItemEvolution> GetItemEvolutions(PKM pk)
    {
        if (IsEmpty(pk) || pk.IsEgg)
            return [];
        EvolutionTree tree;
        try { tree = EvolutionTree.GetEvolutionTree(pk.Context); }
        catch (ArgumentOutOfRangeException) { return []; }
        var items = GetItemNames(pk);
        var list = new List<ItemEvolution>();
        foreach (var m in tree.Forward.GetForward(pk.Species, pk.Form).Span)
        {
            if (m.Method is not (EvolutionType.UseItem or EvolutionType.UseItemMale or EvolutionType.UseItemFemale
                    or EvolutionType.UseItemWormhole or EvolutionType.UseItemFullMoon) || m.Species > pk.MaxSpeciesID)
                continue;
            int item = m.Argument;
            var itemName = item > 0 && item < items.Count && items[item].Length > 0 ? items[item] : $"item #{item}";
            var requirement = m.Method switch
            {
                EvolutionType.UseItemMale => $"usar {itemName} (só macho)",
                EvolutionType.UseItemFemale => $"usar {itemName} (só fêmea)",
                EvolutionType.UseItemWormhole => $"usar {itemName} no Ultra Espaço",
                EvolutionType.UseItemFullMoon => $"usar {itemName} na lua cheia",
                _ => $"usar {itemName}",
            };
            string? blocked = m.Method switch
            {
                EvolutionType.UseItemMale when pk.Gender != 0 => "só machos evoluem assim",
                EvolutionType.UseItemFemale when pk.Gender != 1 => "só fêmeas evoluem assim",
                _ => null,
            };
            list.Add(new ItemEvolution(m.Species, m.GetDestinationForm(pk.Form), SpeciesNames[m.Species], requirement, item, blocked));
        }
        return list;
    }

    /// <summary>Evolui usando o item (como no jogo, o item da mochila e gasto; o item segurado nao muda).</summary>
    public static string EvolveByItem(PKM pk, ItemEvolution evo)
    {
        bool nicknamed = pk.IsNicknamed;
        int abilitySlot = pk.AbilityNumber switch { 2 => 1, 4 => 2, _ => 0 };
        var from = SpeciesNames[pk.Species];
        pk.Species = evo.Species;
        pk.Form = evo.Form;
        if (!nicknamed)
            pk.ClearNickname();
        pk.RefreshAbility(abilitySlot);
        pk.ResetPartyStats();
        pk.RefreshChecksum();
        return $"{from} evoluiu para {evo.Name} ({evo.Requirement})";
    }

    public static string EvolveByTrade(PKM pk, TradeEvolution evo, ITrainerInfo? owner)
    {
        var current = GetTradeEvolutions(pk).FirstOrDefault(e => e.Species == evo.Species && e.Form == evo.Form && e.ItemId == evo.ItemId);
        if (current is null || current.Blocked is not null) throw new InvalidOperationException(current?.Blocked ?? "Esta evolução não está disponível.");
        var handler = pk as IHandlerUpdate;
        bool simulateTrade = handler is not null && owner is not null && pk.Format >= 6 && pk.IsUntraded;
        string partnerName = pk.OriginalTrainerName == "PKHeX" ? "Trade" : "PKHeX";
        if (simulateTrade)
            handler!.UpdateHandler(new SimpleTrainerInfo(owner!.Version) { Language = owner.Language, OT = partnerName });

        bool nicknamed = pk.IsNicknamed;
        int abilitySlot = pk.AbilityNumber switch { 2 => 1, 4 => 2, _ => 0 };
        var from = SpeciesNames[pk.Species];
        pk.Species = evo.Species;
        pk.Form = evo.Form;
        if (!nicknamed)
            pk.ClearNickname();
        if (pk is not PA9) pk.RefreshAbility(abilitySlot); // Z-A conserva a habilidade da origem até passar pelo HOME.
        bool consumed = evo.ItemId > 0 && pk.HeldItem == evo.ItemId;
        if (consumed)
            pk.HeldItem = 0;
        pk.ResetPartyStats();

        if (simulateTrade)
            handler!.UpdateHandler(owner!); // volta para o dono
        if (pk is PA9 pa9)
            pa9.SetPlusFlags(pa9.PersonalInfo, new LegalityAnalysis(pa9), false, false);
        pk.RefreshChecksum();
        return $"{from} evoluiu para {evo.Name} ({evo.Requirement}{(consumed ? "; o item foi consumido" : "")}"
               + $"{(simulateTrade ? $"; parceiro de troca registrado como \"{partnerName}\"" : "")})";
    }

    // Correcoes sugeridas (as mesmas do PKHeX original / Batch Editor). Retornam false se nada mudou.
    /// <summary>Golpes sugeridos (moveset de level-up legal), com PP cheio.</summary>
    public static bool SuggestMoves(PKM pk)
    {
        Span<ushort> before = stackalloc ushort[4];
        pk.GetMoves(before);
        pk.SetMoveset();
        Span<ushort> after = stackalloc ushort[4];
        pk.GetMoves(after);
        return !before.SequenceEqual(after);
    }

    /// <summary>Golpes de reaprender sugeridos pelo encontro (Gen 6+).</summary>
    public static bool SuggestRelearnMoves(PKM pk)
    {
        if (pk.Format < 6)
            return false;
        Span<ushort> before = stackalloc ushort[4];
        pk.GetRelearnMoves(before);
        pk.SetRelearnMoves(new LegalityAnalysis(pk));
        Span<ushort> after = stackalloc ushort[4];
        pk.GetRelearnMoves(after);
        return !before.SequenceEqual(after);
    }

    /// <summary>Local e nivel de encontro sugeridos (sobe o nivel atual se for menor que o minimo). Null = nenhum encontro possivel.</summary>
    public static bool? SuggestMetData(PKM pk)
    {
        var enc = EncounterSuggestion.GetSuggestedMetInfo(pk);
        if (enc is null)
            return null;
        var level = enc.LevelMin;
        var current = Math.Max(EncounterSuggestion.GetLowestLevel(pk, level), level);
        if (pk.MetLevel == level && pk.MetLocation == enc.Location && pk.CurrentLevel >= current)
            return false;
        pk.MetLevel = level;
        pk.MetLocation = enc.Location;
        if (pk.CurrentLevel < current)
            pk.CurrentLevel = current;
        return true;
    }

    /// <summary>
    /// Corrige os avisos "Fishy" que dependem so de valores livres, sem mexer no encontro: EVs zerados apesar de ter
    /// subido de nivel (ganha EVs como em batalhas), EXP exatamente no limiar do nivel, EVs todos iguais e 508 EVs.
    /// So mantem a mudanca se o Pokemon continuar legal. Retorna os avisos corrigidos (0 = nada mudou).
    /// </summary>
    public static int TidyFishy(PKM pk)
    {
        if (pk.IsEgg)
            return 0;
        var la = new LegalityAnalysis(pk);
        if (!la.Valid)
            return 0;
        var codes = la.Results.Where(r => r.Judgement == Severity.Fishy).Select(r => r.Result).ToHashSet();
        int fixedCount = 0;
        Span<int> evs = stackalloc int[6];
        pk.GetEVs(evs);
        Span<int> oldEvs = stackalloc int[6];
        evs.CopyTo(oldEvs);
        var oldExp = pk.EXP;
        int cap = pk.Format >= 6 ? 252 : 255;

        if (codes.Contains(LegalityCheckResultCode.EffortEXPIncreased))
        {
            // EVs proporcionais aos niveis ganhos, puxados para os atributos que a propria especie da (nunca todos iguais).
            var gained = Math.Max(1, pk.CurrentLevel - Math.Max(1, (int)pk.MetLevel));
            var total = Math.Clamp(gained * 4, 8, 300);
            var y = pk.PersonalInfo is IEffortValueYield ey ? new[] { ey.EV_HP, ey.EV_ATK, ey.EV_DEF, ey.EV_SPE, ey.EV_SPA, ey.EV_SPD } : new int[6];
            int sum = 0;
            for (int i = 0; i < 6; i++) { y[i] = y[i] * 3 + 1; sum += y[i]; }
            for (int i = 0; i < 6; i++)
                evs[i] = Math.Min(cap, total * y[i] / sum);
            if (!evs.ContainsAnyExcept(evs[0]))
                evs[0] = Math.Min(cap, evs[0] + 4);
            fixedCount++;
        }
        else if (codes.Contains(LegalityCheckResultCode.EffortAllEqual))
        {
            evs[0] = evs[0] >= 4 ? evs[0] - 4 : evs[0] + 4;
            fixedCount++;
        }
        else if (codes.Contains(LegalityCheckResultCode.Effort2Remaining))
        {
            int i = 0;
            while (i < 6 && evs[i] < 4) i++;
            if (i < 6) { evs[i] -= 4; fixedCount++; }
        }
        pk.SetEVs(evs);

        if (codes.Contains(LegalityCheckResultCode.LevelEXPThreshold) && pk.CurrentLevel < Experience.MaxLevel)
        {
            // Um pouco de EXP a caminho do proximo nivel (como depois de algumas batalhas).
            var growth = pk.PersonalInfo.EXPGrowth;
            var cur = Experience.GetEXP(pk.CurrentLevel, growth);
            var next = Experience.GetEXP((byte)(pk.CurrentLevel + 1), growth);
            pk.EXP = cur + Math.Max(1u, (next - cur) / 3);
            fixedCount++;
        }
        if (fixedCount == 0)
            return 0;
        pk.ResetPartyStats();
        pk.RefreshChecksum();
        var after = new LegalityAnalysis(pk);
        if (!after.Valid)
        {
            pk.SetEVs(oldEvs);
            pk.EXP = oldExp;
            pk.ResetPartyStats();
            pk.RefreshChecksum();
            return 0;
        }
        return fixedCount;
    }

    /// <summary>Quantos avisos "Fishy" o Pokemon tem (legal, mas suspeito).</summary>
    public static int CountFishy(PKM pk) => new LegalityAnalysis(pk).Results.Count(r => r.Judgement == Severity.Fishy);

    /// <summary>
    /// Ate <paramref name="max"/> problemas de legalidade em texto curto: primeiro os invalidos, depois os avisos
    /// "Fishy" (que o relatorio resumido do Core omite). Lista vazia = legal e sem avisos.
    /// </summary>
    public static IReadOnlyList<string> GetLegalityIssues(PKM pk, int max = 4)
    {
        try
        {
            var la = new LegalityAnalysis(pk);
            var ctx = LegalityLocalizationContext.Create(la, GameInfo.CurrentLanguage);
            var invalid = new List<string>();
            var fishy = new List<string>();
            foreach (var chk in la.Results)
            {
                if (chk.Judgement == Severity.Invalid)
                    invalid.Add(ctx.Humanize(chk));
                else if (chk.Judgement == Severity.Fishy)
                    fishy.Add(ctx.Humanize(chk));
            }
            var moves = la.Info.Moves;
            for (int i = 0; i < moves.Length; i++)
            {
                if (!moves[i].Valid)
                    invalid.Add(ctx.FormatMove(moves[i], i + 1, pk.Context));
            }
            if (!la.Valid && invalid.Count == 0)
                invalid.Add("Invalid: " + la.Report().Split('\n')[0].Trim());
            return [.. invalid.Concat(fishy).Take(max)];
        }
        catch (Exception ex)
        {
            return [ex.Message];
        }
    }

    public static (bool Valid, string Report) CheckLegality(PKM pk)
    {
        try
        {
            var la = new LegalityAnalysis(pk);
            return (la.Valid, la.Report());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
