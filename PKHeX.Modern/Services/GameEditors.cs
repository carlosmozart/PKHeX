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
/// Editores por jogo: flags e valores de evento (Gen 2–7 e BD/SP com os nomes das listas do PKHeX; Scarlet/Violet
/// pelos blocos do save que o PKHeX conhece pelo nome), atalhos de eventos do BD/SP e recordes (Gen 3, 5, 6, 7,
/// Sword/Shield e BD/SP). Le e grava direto no save aberto.
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
            default:
                LoadClassic(sav, flags, works, out _getFlag, out _setFlag, out _getWork, out _setWork);
                break;
        }
        Flags = flags;
        Works = works;
        Records = LoadRecords();
    }

    public bool HasEvents => Flags.Count > 0;
    public bool HasRecords => Records.Count > 0;
    public bool HasShortcuts => Shortcuts.Count > 0;
    public bool IsAvailable => HasEvents || Works.Count > 0 || HasRecords || HasShortcuts;

    // Eventos
    public int FlagCount => Flags.Count;
    public int WorkCount => Works.Count;
    /// <summary>Todas as flags (com nome quando a lista do jogo tem).</summary>
    public IReadOnlyList<GameFlag> Flags { get; }
    /// <summary>Todos os valores (com nome quando a lista do jogo tem).</summary>
    public IReadOnlyList<GameWork> Works { get; }
    /// <summary>A lista de nomes do jogo existe (sem ela, tudo aparece só pelo número).</summary>
    public bool HasLabels { get; private set; }
    /// <summary>Atalhos de eventos (so BD/SP por enquanto).</summary>
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

    // Gen 2–7: IEventFlagArray + IEventWorkArray, nomes de flags_xx/const_xx
    private void LoadClassic(SaveFile sav, List<GameFlag> flags, List<GameWork> works,
        out Func<int, bool>? getFlag, out Action<int, bool>? setFlag, out Func<int, long>? getWork, out Action<int, long>? setWork)
    {
        IEventFlagArray? arr = sav switch
        {
            IEventFlag37 f => f,
            IEventFlagProvider37 p => p.EventWork,
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
