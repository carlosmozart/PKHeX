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
        if (!SaveUtil.TryGetSaveFile(path, out var sav))
            return null;
        OnSaveLoaded(sav);
        return sav;
    }

    private static void OnSaveLoaded(SaveFile sav)
    {
        GameInfo.FilteredSources = new FilteredGameDataSource(sav, GameInfo.Sources);
        Drawing.PokeSprite.SpriteUtil.Initialize(sav);
    }

    public static void ExportSave(SaveFile sav, string path)
    {
        var data = sav.Write();
        File.WriteAllBytes(path, data.Span);
    }

    public static string GetGameName(SaveFile sav) => GameInfo.GetVersionName(sav.Version);
    public static string GetVersionName(GameVersion version) => GameInfo.GetVersionName(version);

    public static string GetBoxName(SaveFile sav, int box)
        => sav is IBoxDetailNameRead n ? n.GetBoxName(box) : $"Box {box + 1}";

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
        var p = pk.PersonalInfo;
        var names = GameInfo.Strings.types;
        var list = new List<(string, uint)> { (names[p.Type1], (uint)Drawing.PokeSprite.TypeColor.GetTypeSpriteColor(p.Type1).ToArgb()) };
        if (p.Type2 != p.Type1)
            list.Add((names[p.Type2], (uint)Drawing.PokeSprite.TypeColor.GetTypeSpriteColor(p.Type2).ToArgb()));
        return list;
    }

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
    public static string? MoveSlot(SaveFile sav, ISlotInfo src, ISlotInfo dst, bool copy)
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

    /// <summary>Carrega um arquivo .pk* e converte para o formato do save. Retorna null se nao for compativel.</summary>
    public static PKM? LoadEntityFile(SaveFile sav, string path)
    {
        if (FileUtil.GetSupportedFile(path, sav) is not PKM pk)
            return null;
        if (pk.GetType() == sav.PKMType)
            return pk;
        return EntityConverter.ConvertToType(pk, sav.PKMType, out _);
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
