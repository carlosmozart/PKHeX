using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Flag de evento com o nome que o PKHeX conhece (vazio = sem nome).</summary>
/// <param name="Code">Texto da coluna do numero (null = "#0000"); ex.: "S#0005" nas flags de sistema do BD/SP.</param>
/// <param name="Group">Categoria propria (Scarlet/Violet); null = a do <paramref name="Type"/>.</param>
public sealed record GameFlag(int Index, string Name, NamedEventType Type, string? Code = null, string? Group = null);

/// <summary>Valor de evento (work/const) com nome, valores conhecidos e faixa.</summary>
public sealed record GameWork(int Index, string Name, NamedEventType Type, IReadOnlyList<NamedEventConst> Options,
    long Min = 0, long Max = ushort.MaxValue, string? Code = null, string? Group = null);

/// <summary>Recorde do treinador (passos, batalhas, ovos chocados...).</summary>
public sealed record GameRecord(int Id, string Name, long Max);

/// <summary>Atalho de evento (BD/SP): libera um lendario, reaparece um errante... Ready = ainda da para usar.</summary>
public sealed record GameShortcut(string Name, string Description, Func<bool> Ready, Action Apply);

/// <summary>
/// Editores por jogo: flags e valores de evento (Gen 1–7, Let's Go e BD/SP com os nomes das listas do PKHeX;
/// Scarlet/Violet pelos blocos do save que o PKHeX conhece pelo nome; Z-A pelas tabelas de hash do save), atalhos
/// de eventos (Gen 1, Let's Go, BD/SP e Z-A) e recordes (Gen 3, 5, 6, 7, Sword/Shield e BD/SP). Le e grava direto
/// no save aberto.
/// </summary>
public sealed class GameEditors
{
    private readonly SaveFile _sav;
    private readonly Func<int, bool>? _getFlag;
    private readonly Action<int, bool>? _setFlag;
    private readonly Func<int, long>? _getWork;
    private readonly Action<int, long>? _setWork;

    public GameEditors(SaveFile sav)
    {
        _sav = sav;
        List<GameFlag> flags = [];
        List<GameWork> works = [];
        switch (sav)
        {
            case SAV8BS bs:
                LoadBdsp(bs, flags, works, out _getFlag, out _setFlag, out _getWork, out _setWork);
                Shortcuts = LoadShortcuts8b(bs);
                break;
            case SAV9SV sv:
                LoadScarletViolet(sv, flags, works, out _getFlag, out _setFlag, out _getWork, out _setWork);
                break;
            case SAV7b gg:
                LoadLetsGo(gg, flags, works, out _getFlag, out _setFlag, out _getWork, out _setWork);
                Shortcuts = LoadShortcuts7b(gg);
                break;
            case SAV9ZA za:
                LoadZA(za, flags, works, out _getFlag, out _setFlag, out _getWork, out _setWork);
                Shortcuts = LoadShortcuts9a(za);
                break;
            default:
                LoadClassic(sav, flags, works, out _getFlag, out _setFlag, out _getWork, out _setWork);
                if (sav is SAV1 s1)
                {
                    NameFlags1(s1, flags);
                    Shortcuts = LoadShortcuts1(s1);
                }
                break;
        }
        Flags = flags;
        Works = works;
        Shortcuts = [.. Shortcuts, .. LoadCaseShortcuts(sav)];
        Fame = HallOfFame.Load(sav);
        Records = LoadRecords();
    }

    public bool HasEvents => Flags.Count > 0;
    public bool HasRecords => Records.Count > 0;
    public bool HasShortcuts => Shortcuts.Count > 0;
    public bool HasFame => Fame.Count > 0;
    public bool IsAvailable => HasEvents || Works.Count > 0 || HasRecords || HasShortcuts || HasFame;
    /// <summary>Equipes do Hall da Fama (so leitura).</summary>
    public IReadOnlyList<FameTeam> Fame { get; } = [];

    // Eventos
    public int FlagCount => Flags.Count;
    public int WorkCount => Works.Count;
    /// <summary>Todas as flags (com nome quando a lista do jogo tem).</summary>
    public IReadOnlyList<GameFlag> Flags { get; }
    /// <summary>Todos os valores (com nome quando a lista do jogo tem).</summary>
    public IReadOnlyList<GameWork> Works { get; }
    /// <summary>A lista de nomes do jogo existe (sem ela, tudo aparece só pelo número).</summary>
    public bool HasLabels { get; private set; }
    /// <summary>Atalhos de eventos (Gen 1, Let's Go, BD/SP e Z-A).</summary>
    public IReadOnlyList<GameShortcut> Shortcuts { get; } = [];

    public bool GetFlag(int index) => _getFlag!(index);
    public void SetFlag(int index, bool value) => _setFlag!(index, value);
    public long GetWork(int index) => _getWork!(index);
    public void SetWork(int index, long value)
    {
        var w = Works[index];
        _setWork!(index, Math.Clamp(value, w.Min, w.Max));
    }

    /// <summary>Categoria em portugues da flag/valor (a propria, no Scarlet/Violet, ou a do tipo do PKHeX).</summary>
    public static string CategoryOf(GameFlag f) => f.Group ?? (f.Type == NamedEventType.None ? "" : CategoryName(f.Type));
    public static string CategoryOf(GameWork w) => w.Group ?? (w.Type == NamedEventType.None ? "" : CategoryName(w.Type));

    // Gen 1–7: IEventFlagArray + IEventWorkArray, nomes de flags_xx/const_xx (o Gen 1 nao tem lista)
    private void LoadClassic(SaveFile sav, List<GameFlag> flags, List<GameWork> works,
        out Func<int, bool>? getFlag, out Action<int, bool>? setFlag, out Func<int, long>? getWork, out Action<int, long>? setWork)
    {
        IEventFlagArray? arr = sav switch
        {
            IEventFlag37 f => f,
            IEventFlagProvider37 p => p.EventWork,
            SAV1 s => s,
            SAV2 s => s,
            _ => null,
        };
        getFlag = null; setFlag = null; getWork = null; setWork = null;
        if (arr is null)
            return;
        getFlag = arr.GetEventFlag;
        setFlag = arr.SetEventFlag;
        int workCount = 0, workMax = 0;
        switch (arr)
        {
            case IEventWorkArray<ushort> u:
                getWork = i => u.GetWork(i);
                setWork = (i, v) => u.SetWork(i, (ushort)v);
                (workCount, workMax) = (u.EventWorkCount, ushort.MaxValue);
                break;
            case IEventWorkArray<byte> b:
                getWork = i => b.GetWork(i);
                setWork = (i, v) => b.SetWork(i, (byte)v);
                (workCount, workMax) = (b.EventWorkCount, byte.MaxValue);
                break;
        }
        int flagCount = arr.EventFlagCount;
        Dictionary<int, NamedEventValue> flagNames = [];
        Dictionary<int, NamedEventWork> workNames = [];
        if (GetLabelSuffix(sav.Version) is { } game)
        {
            try
            {
                var labels = new EventLabelCollection(game, flagCount, workCount);
                foreach (var f in labels.Flag)
                    flagNames.TryAdd(f.Index, f);
                foreach (var w in labels.Work)
                    workNames.TryAdd(w.Index, w);
                HasLabels = flagNames.Count + workNames.Count > 0;
            }
            catch
            {
                // sem lista de nomes: fica so com os numeros
            }
        }
        for (int i = 0; i < flagCount; i++)
            flags.Add(flagNames.TryGetValue(i, out var n) ? new GameFlag(i, n.Name, n.Type) : new GameFlag(i, "", NamedEventType.None));
        for (int i = 0; i < workCount; i++)
            works.Add(workNames.TryGetValue(i, out var n) ? new GameWork(i, n.Name, n.Type, n.PredefinedValues, 0, workMax) : new GameWork(i, "", NamedEventType.None, [], 0, workMax));
    }

    // BD/SP: FlagWork8b (4000 flags, 1000 flags de sistema e 500 valores int32), nomes de flags/system/work_bdsp
    private void LoadBdsp(SAV8BS bs, List<GameFlag> flags, List<GameWork> works,
        out Func<int, bool>? getFlag, out Action<int, bool>? setFlag, out Func<int, long>? getWork, out Action<int, long>? setWork)
    {
        var fw = bs.FlagWork;
        int nFlag = fw.CountFlag, nSystem = fw.CountSystem, nWork = fw.CountWork;
        Dictionary<int, NamedEventValue> flagNames = [], systemNames = [];
        Dictionary<int, NamedEventWork> workNames = [];
        try
        {
            var labels = new EventLabelCollectionSystem("bdsp", nFlag - 1, nSystem - 1, nWork - 1);
            foreach (var f in labels.Flag) flagNames.TryAdd(f.Index, f);
            foreach (var f in labels.System) systemNames.TryAdd(f.Index, f);
            foreach (var w in labels.Work) workNames.TryAdd(w.Index, w);
            HasLabels = flagNames.Count + systemNames.Count + workNames.Count > 0;
        }
        catch
        {
            // sem lista de nomes
        }
        // Flags normais primeiro (indice 0..3999); as de sistema depois (indice 4000..4999, mostradas como S#0000).
        for (int i = 0; i < nFlag; i++)
            flags.Add(flagNames.TryGetValue(i, out var n) ? new GameFlag(i, n.Name, n.Type) : new GameFlag(i, "", NamedEventType.None));
        for (int i = 0; i < nSystem; i++)
            flags.Add(systemNames.TryGetValue(i, out var n)
                ? new GameFlag(nFlag + i, n.Name, n.Type, $"S#{i:0000}", "Sistema")
                : new GameFlag(nFlag + i, "", NamedEventType.None, $"S#{i:0000}", "Sistema"));
        for (int i = 0; i < nWork; i++)
            works.Add(workNames.TryGetValue(i, out var n)
                ? new GameWork(i, n.Name, n.Type, n.PredefinedValues, int.MinValue, int.MaxValue)
                : new GameWork(i, "", NamedEventType.None, [], int.MinValue, int.MaxValue));
        getFlag = i => i < nFlag ? fw.GetFlag(i) : fw.GetSystemFlag(i - nFlag);
        setFlag = (i, v) => { if (i < nFlag) fw.SetFlag(i, v); else fw.SetSystemFlag(i - nFlag, v); };
        getWork = i => fw.GetWork(i);
        setWork = (i, v) => fw.SetWork(i, (int)v);
    }

    /// <summary>Atalhos do PKHeX para o BD/SP (EventUnlocker8b): lendarios, errantes, zonas do mapa e roupas.</summary>
    private static List<GameShortcut> LoadShortcuts8b(SAV8BS bs)
    {
        var u = new EventUnlocker8b(bs);
        return
        [
            new("Revanche com Dialga/Palkia", "Libera de novo o lendário da capa na Spear Pillar (depois de capturado ou derrotado).", () => u.UnlockReadyBoxLegend, u.UnlockBoxLegend),
            new("Evento do Darkrai", "Dá o Member Card, a Pokédex Nacional e libera o Darkrai em Newmoon Island.", () => u.UnlockReadyDarkrai, u.UnlockDarkrai),
            new("Evento do Shaymin", "Dá a Oak's Letter, a Pokédex Nacional e libera o Shaymin na Flower Paradise (precisa ter terminado o jogo).", () => u.UnlockReadyShaymin, u.UnlockShaymin),
            new("Evento do Arceus", "Dá a Azure Flute, a Pokédex Nacional e libera o Arceus na Hall of Origin.", () => u.UnlockReadyArceus, u.UnlockArceus),
            new("Spiritomb", "Marca as 32 conversas no Grand Underground necessárias para o Spiritomb aparecer na Hallowed Tower.", () => u.UnlockReadySpiritomb, u.UnlockSpiritomb),
            new("Mesprit errante de novo", "Faz o Mesprit voltar a vagar (depois de capturado ou derrotado).", () => u.ResetReadyRoamerMesprit, u.RespawnMesprit),
            new("Cresselia errante de novo", "Faz a Cresselia voltar a vagar (depois de capturada ou derrotada).", () => u.ResetReadyRoamerCresselia, u.RespawnCresselia),
            new("Todas as áreas do mapa", "Marca todas as cidades e áreas como visitadas (voo) e revela as ilhas e caminhos escondidos.", () => true, u.UnlockZones),
            new("Todas as roupas", "Libera todas as roupas do provador.", () => true, u.UnlockFashion),
        ];
    }

    // Gen 1: o PKHeX so conhece as flags dos Pokemon fixos do mapa (G1OverworldSpawner); da nome a elas.
    private static void NameFlags1(SAV1 sav, List<GameFlag> flags)
    {
        foreach (var (name, eventFlag, _) in GetSpawns1(sav))
        {
            if (eventFlag > 0 && eventFlag < flags.Count && flags[eventFlag].Name.Length == 0)
                flags[eventFlag] = flags[eventFlag] with { Name = $"{name}: já obtido ou derrotado", Type = NamedEventType.EventEncounter };
        }
    }

    /// <summary>Pokemon fixos do mapa no Gen 1: nome ("Voltorb 3"), flag de evento e o par do Core.</summary>
    private static IEnumerable<(string Name, int EventFlag, FlagPairG1Detail Pair)> GetSpawns1(SAV1 sav)
    {
        var spawner = new G1OverworldSpawner(sav);
        foreach (var pair in spawner.GetFlagPairs())
        {
            var name = pair.Name[G1OverworldSpawner.FlagPropertyPrefix.Length..].Replace('_', ' ');
            var backing = typeof(FlagPairG1Detail).GetField("Backing", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pair)
                ?? typeof(FlagPairG1Detail).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault(f => f.FieldType == typeof(FlagPairG1))?.GetValue(pair);
            var eventFlag = backing is FlagPairG1 b
                ? (int)(typeof(FlagPairG1).GetField("EventFlag", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(b) ?? 0)
                : 0;
            yield return (name, eventFlag, pair);
        }
    }

    /// <summary>Onde cada Pokemon fixo do Gen 1 aparece (para a explicacao do atalho).</summary>
    private static string Place1(string species) => species switch
    {
        "Mewtwo" => "Cerulean Cave",
        "Articuno" => "Seafoam Islands",
        "Zapdos" or "Voltorb" or "Electrode" => "Power Plant",
        "Moltres" => "Victory Road",
        "Hitmonlee" or "Hitmonchan" => "Fighting Dojo, Saffron City",
        "Eevee" => "Celadon Mansion",
        "Kabuto" or "Omanyte" => "Mt. Moon",
        "Aerodactyl" => "Pewter Museum",
        "Bulbasaur" => "Cerulean City",
        "Squirtle" => "Vermilion City",
        "Charmander" => "Route 24",
        _ => "",
    };

    /// <summary>Atalhos do Gen 1: faz os Pokemon fixos do mapa (lendarios, presentes, Voltorbs...) aparecerem de novo.</summary>
    private static List<GameShortcut> LoadShortcuts1(SAV1 sav)
    {
        List<GameShortcut> list = [];
        foreach (var group in GetSpawns1(sav).GroupBy(s => s.Name.Split(' ')[0]).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var species = group.Key;
            var names = group.Select(g => g.Name).ToHashSet();
            var place = Place1(species);
            var many = names.Count > 1;
            var desc = many
                ? $"Faz os {species} voltarem a aparecer ({place}), depois de capturados, derrotados ou recebidos."
                : $"Faz o {species} voltar a aparecer ({place}), depois de capturado, derrotado ou recebido.";
            // Cada uso recria o spawner do Core: ele copia as flags do save e grava todas de volta no Save().
            IEnumerable<FlagPairG1Detail> Pairs(G1OverworldSpawner o) => o.GetFlagPairs()
                .Where(p => names.Contains(p.Name[G1OverworldSpawner.FlagPropertyPrefix.Length..].Replace('_', ' ')));
            list.Add(new GameShortcut(many ? $"{species} de novo ({names.Count})" : $"{species} de novo", desc,
                () => Pairs(new G1OverworldSpawner(sav)).Any(p => p.IsHidden),
                () =>
                {
                    var o = new G1OverworldSpawner(sav);
                    foreach (var p in Pairs(o))
                        p.Reset();
                    o.Save();
                }));
        }
        return list;
    }

    /// <summary>
    /// Atalhos dos estojos e do Pokeathlon (os botoes "dar todos" do PKHeX): Pokeblocks (Ruby/Sapphire/Emerald e
    /// Omega Ruby/Alpha Sapphire), Poffins (Diamond/Pearl/Platinum e BD/SP) e Pokeathlon (HeartGold/SoulSilver).
    /// </summary>
    private static List<GameShortcut> LoadCaseShortcuts(SaveFile sav)
    {
        List<GameShortcut> list = [];
        switch (sav)
        {
            case SAV3 { LargeBlock: ISaveBlock3LargeHoenn hoenn }:
                list.Add(new("Estojo de Pokéblocks cheio", "Enche o estojo com 40 Pokéblocks dourados de sabor e maciez máximos (para concursos).",
                    () => hoenn.PokeBlocks.Blocks.Any(b => b.Color != PokeBlock3Color.Gold || b.Level != 255 || b.Feel != 255),
                    () => { var c = hoenn.PokeBlocks; c.MaximizeAll(true); hoenn.PokeBlocks = c; }));
                break;
            case SAV4Sinnoh sinnoh:
                list.Add(new("Estojo de Poffins cheio", "Enche o estojo com 100 Poffins de todos os sabores no máximo (para concursos).",
                    () => new PoffinCase4(sinnoh).Poffins.Any(p => p.Type != PoffinFlavor4.Rich || p.Smoothness != 255 || p.BoostSpicy != 255),
                    () => { var c = new PoffinCase4(sinnoh); c.FillCase(); c.Save(); }));
                break;
            case SAV4HGSS hgss:
                list.Add(new("Pontos do Pokéathlon no máximo", $"Deixa os pontos do Pokéathlon em {Pokeathlon4.MaxPoints:N0} (para a loja do Athlon Dome).",
                    () => hgss.Pokeathlon.Points < Pokeathlon4.MaxPoints, () => { var a = hgss.Pokeathlon; a.Points = Pokeathlon4.MaxPoints; }));
                list.Add(new("Todos os Data Cards do Pokéathlon", "Marca os 27 Data Cards do Pokéathlon como obtidos.",
                    () => hgss.Pokeathlon.FlagsDataCard != Pokeathlon4.DataCardAllObtained, () => { var a = hgss.Pokeathlon; a.FlagsDataCard = Pokeathlon4.DataCardAllObtained; }));
                list.Add(new("Todas as medalhas do Pokéathlon", "Dá as medalhas dos 5 cursos do Pokéathlon para todas as espécies (Bulbasaur a Arceus).",
                    () => hgss.Pokeathlon.Medals.GetTotalCount() < PokeathlonMedalManager4.SIZE * 5, () => hgss.Pokeathlon.Medals.SetAllMedals()));
                break;
            case SAV6AO ao:
                list.Add(new("999 Pokéblocks de cada", "Deixa 999 Pokéblocks de cada uma das 12 cores no estojo (para concursos).",
                    () => Enumerable.Range(0, Contest6.CountBlock).Any(i => ao.Contest.GetBlockCount(i) < Contest6.MaxBlock),
                    () => { for (int i = 0; i < Contest6.CountBlock; i++) ao.Contest.SetBlockCount(i, Contest6.MaxBlock); }));
                break;
            case SAV8BS bs:
                list.Add(new("Estojo de Poffins cheio", "Enche o estojo com 100 Poffins de nível 60 e todos os sabores no máximo (para concursos).",
                    () => bs.Poffins.GetPoffins().Any(p => p.MstID != 0x1C || p.Level != 60 || p.Taste != 0xFF),
                    () =>
                    {
                        var all = bs.Poffins.GetPoffins();
                        foreach (var p in all)
                        {
                            p.MstID = 0x1C;
                            p.Level = 60;
                            p.Taste = 0xFF;
                            p.FlavorSpicy = p.FlavorBitter = p.FlavorDry = p.FlavorSour = p.FlavorSweet = 0xFF;
                        }
                        bs.Poffins.SetPoffins(all);
                    }));
                break;
        }
        return list;
    }

    // Let's Go: EventWork7b (4096 flags e 1000 valores int32 divididos em zona, sistema, objetos/cenas e eventos),
    // nomes de flags_gg/const_gg (que numeram cada tipo separado: "v0277" = objeto 277).
    private void LoadLetsGo(SAV7b gg, List<GameFlag> flags, List<GameWork> works,
        out Func<int, bool>? getFlag, out Action<int, bool>? setFlag, out Func<int, long>? getWork, out Action<int, long>? setWork)
    {
        var ev = gg.Blocks.EventWork;
        Dictionary<int, string> flagNames = [], workNames = [];
        try
        {
            var editor = new SplitEventEditor<int>(ev,
                GameLanguage.GetStrings("gg", GameInfo.CurrentLanguage, "const"),
                GameLanguage.GetStrings("gg", GameInfo.CurrentLanguage, "flags"));
            foreach (var v in editor.Flag.SelectMany(g => g.Vars))
                flagNames.TryAdd(v.RawIndex, v.Name);
            foreach (var v in editor.Work.SelectMany(g => g.Vars))
                workNames.TryAdd(v.RawIndex, v.Name);
            HasLabels = flagNames.Count + workNames.Count > 0;
        }
        catch
        {
            // sem lista de nomes
        }
        for (int i = 0; i < ev.CountFlag; i++)
        {
            var (prefix, group) = Group7b(ev.GetFlagType(i, out var sub), flag: true);
            flags.Add(new GameFlag(i, flagNames.GetValueOrDefault(i, ""), NamedEventType.None, $"{prefix}#{sub:0000}", group));
        }
        // Os ultimos 72 valores nao pertencem a nenhum tipo (sem uso no jogo)
        for (int i = 0; i < ev.CountWork; i++)
        {
            int sub = i;
            var (prefix, group) = i < 928 ? Group7b(ev.GetWorkType(i, out sub), flag: false) : ("U", "Sem uso");
            if (i >= 928) sub = i - 928;
            works.Add(new GameWork(i, workNames.GetValueOrDefault(i, ""), NamedEventType.None, [], int.MinValue, int.MaxValue, $"{prefix}#{sub:000}", group));
        }
        getFlag = ev.GetFlag;
        setFlag = (i, v) => ev.SetFlag(i, v);
        getWork = i => ev.GetWork(i);
        setWork = (i, v) => ev.SetWork(i, (int)v);
    }

    private static (string Prefix, string Group) Group7b(EventVarType type, bool flag) => type switch
    {
        EventVarType.Zone => ("Z", "Zonas"),
        EventVarType.System => ("S", "Sistema"),
        EventVarType.Vanish => flag ? ("V", "Objetos do mapa") : ("C", "Cenas"),
        _ => ("E", "Eventos"),
    };

    /// <summary>Atalhos do Let's Go: titulos de Mestre Treinador.</summary>
    private static List<GameShortcut> LoadShortcuts7b(SAV7b gg)
    {
        var ev = gg.Blocks.EventWork;
        return
        [
            new("Todos os títulos de Mestre Treinador", "Libera os títulos de Mestre Treinador de todas as espécies (como se tivesse vencido cada um).",
                () => Enumerable.Range(0, EventWork7b.MaxTitleFlag).Any(i => !ev.GetTitleFlag(i)), ev.UnlockAllTitleFlags),
        ];
    }

    // Z-A: flags e valores ficam em tabelas (hash de 64 bits, valor). O jogo nao guarda os nomes; o PKHeX so conhece
    // os itens do mapa (Colorful Screws e TMs). Mostra as entradas usadas de cada tabela, com o hash como codigo.
    private void LoadZA(SAV9ZA za, List<GameFlag> flags, List<GameWork> works,
        out Func<int, bool>? getFlag, out Action<int, bool>? setFlag, out Func<int, long>? getWork, out Action<int, long>? setWork)
    {
        var b = za.Blocks;
        var fieldNames = new Dictionary<ulong, string>();
        var screw = GameInfo.Strings.Item[ColorfulScrew9a.ColorfulScrewItemIndex];
        foreach (var (item, _) in ColorfulScrew9a.GetScrewLocations(b.FieldItems, false).Concat(ColorfulScrew9a.GetScrewLocations(b.FieldItems, true)))
            fieldNames.TryAdd(FnvHash.HashFnv1a_64(item), $"{screw} ({item})");
        foreach (var (item, id, _) in TechnicalMachine9a.TechnicalMachines)
            fieldNames.TryAdd(FnvHash.HashFnv1a_64(item), $"{GameInfo.Strings.Item[id]} ({item})");

        var flagSlots = new List<(EventWorkFlagStorage Table, int Slot)>();
        void AddFlags(EventWorkFlagStorage table, string group, bool named)
        {
            for (int i = 0; i < table.CountUsed; i++)
            {
                var key = table.GetKey(i);
                flags.Add(new GameFlag(flags.Count, named ? fieldNames.GetValueOrDefault(key, "") : "", NamedEventType.None, $"{key:X16}", group));
                flagSlots.Add((table, i));
            }
        }
        AddFlags(b.Event, "Eventos", false);
        AddFlags(b.Flags, "Sistema", false);
        AddFlags(b.FieldItems, "Itens do mapa", true);

        var workSlots = new List<(EventWorkValueStorage Table, int Slot)>();
        void AddWorks(EventWorkValueStorage table, string group)
        {
            for (int i = 0; i < table.CountUsed; i++)
            {
                works.Add(new GameWork(works.Count, "", NamedEventType.None, [], long.MinValue, long.MaxValue, $"{table.GetKey(i):X16}", group));
                workSlots.Add((table, i));
            }
        }
        AddWorks(b.Work, "Sistema");
        AddWorks(b.Quest, "Missões");
        AddWorks(b.WorkMable, "Tarefas da Mable");
        AddWorks(b.CountMable, "Contagens da Mable");
        AddWorks(b.CountTitle, "Títulos");

        HasLabels = fieldNames.Count > 0;
        getFlag = i => flagSlots[i].Table.GetValue(flagSlots[i].Slot);
        setFlag = (i, v) => flagSlots[i].Table.SetValue(flagSlots[i].Slot, v);
        getWork = i => unchecked((long)workSlots[i].Table.GetValue(workSlots[i].Slot));
        setWork = (i, v) => workSlots[i].Table.SetValue(workSlots[i].Slot, unchecked((ulong)v));
    }

    /// <summary>Atalhos do Z-A (os mesmos botoes do Treinador do PKHeX): Colorful Screws e TMs do mapa.</summary>
    private static List<GameShortcut> LoadShortcuts9a(SAV9ZA za)
    {
        var field = za.Blocks.FieldItems;
        bool TmMissing() => TechnicalMachine9a.TechnicalMachines
            .Select(t => field.GetIndex(FnvHash.HashFnv1a_64(t.FieldItem)))
            .Any(i => i != -1 && !field.GetValue(i));
        return
        [
            new("Pegar todos os Colorful Screws", "Marca como pegos os Colorful Screws que faltam no mapa e põe a quantidade na mochila (até 100).",
                () => ColorfulScrew9a.GetScrewLocations(za, false).Any(), () => ColorfulScrew9a.CollectScrews(za)),
            new("Pegar as TMs do mapa", "Marca como pegas as TMs espalhadas pelo mapa e põe cada uma na mochila.",
                TmMissing, () => TechnicalMachine9a.SetAllTechnicalMachines(za, true)),
        ];
    }

    // Scarlet/Violet: cada evento e um bloco do save. Usa os blocos que o PKHeX conhece pelo nome
    // (constantes de SaveBlockAccessor9SV): booleanos viram flags, numeros inteiros viram valores.
    private void LoadScarletViolet(SAV9SV sv, List<GameFlag> flags, List<GameWork> works,
        out Func<int, bool>? getFlag, out Action<int, bool>? setFlag, out Func<int, long>? getWork, out Action<int, long>? setWork)
    {
        var flagBlocks = new List<SCBlock>();
        var workBlocks = new List<SCBlock>();
        var seen = new HashSet<uint>();
        foreach (var (name, key) in GetNamedKeys9())
        {
            if (!seen.Add(key) || name.StartsWith("SUSHI_DAMMY", StringComparison.Ordinal) || !sv.Accessor.TryGetBlock(key, out var block))
                continue;
            var group = Group9(name);
            var label = Humanize9(name);
            if (block.Type is SCTypeCode.Bool1 or SCTypeCode.Bool2)
            {
                flags.Add(new GameFlag(flags.Count, label, NamedEventType.None, $"{key:X8}", group));
                flagBlocks.Add(block);
            }
            else if (Range9(block.Type) is { } range)
            {
                works.Add(new GameWork(works.Count, label, NamedEventType.None, [], range.Min, range.Max, $"{key:X8}", group));
                workBlocks.Add(block);
            }
        }
        HasLabels = flags.Count + works.Count > 0;
        getFlag = i => flagBlocks[i].Type == SCTypeCode.Bool2;
        setFlag = (i, v) => flagBlocks[i].ChangeBooleanType(v ? SCTypeCode.Bool2 : SCTypeCode.Bool1);
        getWork = i => Convert.ToInt64(workBlocks[i].GetValue());
        setWork = (i, v) =>
        {
            var b = workBlocks[i];
            object value = b.Type switch
            {
                SCTypeCode.Byte => (byte)v,
                SCTypeCode.SByte => (sbyte)v,
                SCTypeCode.UInt16 => (ushort)v,
                SCTypeCode.Int16 => (short)v,
                SCTypeCode.UInt32 => (uint)v,
                _ => (int)v,
            };
            b.SetValue(value);
        };
    }

    private static List<(string Name, uint Key)>? _keys9;
    private static List<(string Name, uint Key)> GetNamedKeys9() => _keys9 ??= [.. typeof(SaveBlockAccessor9SV)
        .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(f => f.IsLiteral && f.FieldType == typeof(uint))
        .Select(f => (f.Name, (uint)f.GetRawConstantValue()!))];

    /// <summary>Faixa editavel de um bloco numerico (64 bits e decimais ficam de fora: sementes e tempos internos).</summary>
    private static (long Min, long Max)? Range9(SCTypeCode type) => type switch
    {
        SCTypeCode.Byte => (byte.MinValue, byte.MaxValue),
        SCTypeCode.SByte => (sbyte.MinValue, sbyte.MaxValue),
        SCTypeCode.UInt16 => (ushort.MinValue, ushort.MaxValue),
        SCTypeCode.Int16 => (short.MinValue, short.MaxValue),
        SCTypeCode.UInt32 => (uint.MinValue, uint.MaxValue),
        SCTypeCode.Int32 => (int.MinValue, int.MaxValue),
        _ => null,
    };

    /// <summary>Categoria (em portugues) pelo nome do bloco do Scarlet/Violet.</summary>
    private static string Group9(string name)
    {
        static bool P(string n, string p) => n.StartsWith(p, StringComparison.Ordinal);
        return name switch
        {
            _ when P(name, "FSYS_YMAP_FLY") => "Voo",
            _ when P(name, "FSYS_YMAP") => "Mapa",
            _ when P(name, "FSYS_TIPS") => "Dicas",
            _ when P(name, "KCanCraftTM") => "Receitas de TM",
            _ when P(name, "KCan") || P(name, "KUnlocked") || P(name, "KHas") => "Recursos liberados",
            _ when P(name, "KRemovedStake") || P(name, "KStakes") || P(name, "KShrine") => "Estacas (Treasures of Ruin)",
            _ when P(name, "KPurchased") => "Compras",
            _ when P(name, "KAuction") => "Leilão (Porto Marinada)",
            _ when P(name, "KOutbreak") => "Surtos",
            _ when P(name, "KCaptured") => "Encontros",
            _ when P(name, "KCleared") || P(name, "KChallenged") || P(name, "KBattled") || P(name, "KCompleted") || P(name, "KIndexReceivedBadge") => "Batalhas e desafios",
            _ when P(name, "KSchool") || P(name, "WSYS_SCHOOL") => "Academia",
            _ when P(name, "KPicture") => "Perfil",
            _ when P(name, "WEVT") || P(name, "FEVT") || P(name, "WSYS") || P(name, "FSYS") => "História e eventos",
            _ => "Diversos",
        };
    }

    /// <summary>"KCanCraftTM001" vira "Can Craft TM001"; nomes internos do jogo (FSYS_..., WEVT_...) ficam como estao.</summary>
    private static string Humanize9(string name)
    {
        if (name.Length < 2 || name[0] != 'K' || !char.IsUpper(name[1]) || name.Contains('_'))
            return name;
        var sb = new System.Text.StringBuilder();
        var s = name[1..];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool boundary = i > 0 && (
                (char.IsUpper(c) && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]) && char.IsUpper(s[i - 1]))))
                || (char.IsDigit(c) && char.IsLower(s[i - 1])));
            if (boundary)
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Sufixo das listas de nomes do Core (flags_xx_en.txt / const_xx_en.txt).</summary>
    private static string? GetLabelSuffix(GameVersion version) => version switch
    {
        GameVersion.X or GameVersion.Y or GameVersion.XY => "xy",
        GameVersion.OR or GameVersion.AS or GameVersion.ORAS => "oras",
        GameVersion.SN or GameVersion.MN or GameVersion.SM => "sm",
        GameVersion.US or GameVersion.UM or GameVersion.USUM => "usum",
        GameVersion.D or GameVersion.P or GameVersion.DP => "dp",
        GameVersion.Pt or GameVersion.DPPt => "pt",
        GameVersion.HG or GameVersion.SS or GameVersion.HGSS => "hgss",
        GameVersion.B or GameVersion.W or GameVersion.BW => "bw",
        GameVersion.B2 or GameVersion.W2 or GameVersion.B2W2 => "b2w2",
        GameVersion.R or GameVersion.S or GameVersion.RS => "rs",
        GameVersion.E => "e",
        GameVersion.FR or GameVersion.LG or GameVersion.FRLG => "frlg",
        GameVersion.C => "c",
        GameVersion.GD or GameVersion.SI or GameVersion.GS => "gs",
        _ => null,
    };

    // Recordes
    public IReadOnlyList<GameRecord> Records { get; }

    private List<GameRecord> LoadRecords()
    {
        try
        {
            switch (_sav)
            {
                case SAV3 s3:
                    return [.. Record3.GetItems(s3).Select(c => new GameRecord(c.Value, c.Text, uint.MaxValue))];
                case SAV5 s5:
                    var list = new List<GameRecord>();
                    for (int i = 0; i < Record5.Record32 + Record5.Record16; i++)
                        list.Add(new GameRecord(i, RecordName(RecordLists.RecordList_5, i), i < Record5.Record32 ? Record5.Max32 : Record5.Max16));
                    return list;
                case ITrainerStatRecord t:
                    var names = _sav switch
                    {
                        SAV6 => RecordLists.RecordList_6,
                        SAV7 => RecordLists.RecordList_7,
                        SAV8SWSH => RecordLists.RecordList_8,
                        SAV8BS => Record8b.RecordList_8b,
                        _ => [],
                    };
                    return [.. Enumerable.Range(0, t.RecordCount).Select(i => new GameRecord(i, RecordName(names, i), Math.Max(t.GetRecordMax(i), 0)))];
            }
        }
        catch
        {
            // bloco de recordes ilegivel: a aba nao aparece
        }
        return [];
    }

    private static string RecordName(Dictionary<int, string> names, int i)
        => names.TryGetValue(i, out var n) && n.Length > 0 && !n.StartsWith("???") ? n : "";

    public long GetRecord(GameRecord r) => _sav switch
    {
        SAV3 s3 => s3.GetRecord(r.Id),
        SAV5 s5 => WithRecords5(s5, rec => r.Id < Record5.Record32 ? rec.GetRecord32(r.Id) : (long)rec.GetRecord16(r.Id - Record5.Record32)),
        ITrainerStatRecord t => t.GetRecord(r.Id),
        _ => 0,
    };

    public void SetRecord(GameRecord r, long value)
    {
        value = Math.Clamp(value, 0, r.Max);
        switch (_sav)
        {
            case SAV3 s3: s3.SetRecord(r.Id, (uint)value); break;
            case SAV5 s5:
                WithRecords5(s5, rec =>
                {
                    if (r.Id < Record5.Record32)
                        rec.SetRecord32(r.Id, (uint)value);
                    else
                        rec.SetRecord16(r.Id - Record5.Record32, (ushort)value);
                    return 0L;
                });
                break;
            case ITrainerStatRecord t: t.SetRecord(r.Id, (int)value); break;
        }
    }

    // Na Gen 5 o bloco de recordes fica cifrado no save: decifra para ler/gravar e cifra de novo logo depois.
    private static long WithRecords5(SAV5 sav, Func<Record5, long> action)
    {
        var rec = sav.Records;
        try { return action(rec); }
        finally { rec.EndAccess(); }
    }

    /// <summary>Nome da categoria em portugues.</summary>
    public static string CategoryName(NamedEventType type) => type switch
    {
        NamedEventType.HiddenItem => "Itens escondidos",
        NamedEventType.TrainerToggle => "Treinadores",
        NamedEventType.StoryProgress => "História",
        NamedEventType.FlyToggle => "Voo",
        NamedEventType.Misc => "Diversos",
        NamedEventType.Statistic => "Estatísticas",
        NamedEventType.Achievement => "Conquistas",
        NamedEventType.UsefulFeature => "Úteis",
        NamedEventType.EventEncounter => "Encontros",
        NamedEventType.GiftAvailable => "Presentes",
        NamedEventType.Rebattle => "Revanche",
        _ => "Sem categoria",
    };
}
