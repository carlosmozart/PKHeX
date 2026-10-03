using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Flag de evento com o nome que o PKHeX conhece (vazio = sem nome).</summary>
public sealed record GameFlag(int Index, string Name, NamedEventType Type);

/// <summary>Valor de evento (work/const) com nome e valores conhecidos.</summary>
public sealed record GameWork(int Index, string Name, NamedEventType Type, IReadOnlyList<NamedEventConst> Options);

/// <summary>Recorde do treinador (passos, batalhas, ovos chocados...).</summary>
public sealed record GameRecord(int Id, string Name, long Max);

/// <summary>
/// Editores por jogo: flags e valores de evento (Gen 2–7, com os nomes das listas do PKHeX) e recordes
/// (Gen 3, 5, 6, 7, Sword/Shield e BD/SP). Le e grava direto no save aberto.
/// </summary>
public sealed class GameEditors
{
    private readonly SaveFile _sav;
    private readonly IEventFlagArray? _flags;
    private readonly Func<int, int>? _getWork;
    private readonly Action<int, int>? _setWork;

    public GameEditors(SaveFile sav)
    {
        _sav = sav;
        _flags = sav switch
        {
            IEventFlag37 f => f,
            IEventFlagProvider37 p => p.EventWork,
            SAV2 s => s,
            _ => null,
        };
        switch (_flags)
        {
            case IEventWorkArray<ushort> u:
                _getWork = i => u.GetWork(i);
                _setWork = (i, v) => u.SetWork(i, (ushort)Math.Clamp(v, 0, ushort.MaxValue));
                WorkMax = ushort.MaxValue;
                WorkCount = u.EventWorkCount;
                break;
            case IEventWorkArray<byte> b:
                _getWork = i => b.GetWork(i);
                _setWork = (i, v) => b.SetWork(i, (byte)Math.Clamp(v, 0, byte.MaxValue));
                WorkMax = byte.MaxValue;
                WorkCount = b.EventWorkCount;
                break;
        }
        FlagCount = _flags?.EventFlagCount ?? 0;
        (Flags, Works) = LoadLabels();
        Records = LoadRecords();
    }

    public bool HasEvents => FlagCount > 0;
    public bool HasRecords => Records.Count > 0;
    public bool IsAvailable => HasEvents || HasRecords;

    // Eventos
    public int FlagCount { get; }
    public int WorkCount { get; }
    public int WorkMax { get; }
    /// <summary>Todas as flags (com nome quando a lista do jogo tem).</summary>
    public IReadOnlyList<GameFlag> Flags { get; }
    /// <summary>Todos os valores (com nome quando a lista do jogo tem).</summary>
    public IReadOnlyList<GameWork> Works { get; }
    /// <summary>A lista de nomes do jogo existe (sem ela, tudo aparece só pelo número).</summary>
    public bool HasLabels { get; private set; }

    public bool GetFlag(int index) => _flags!.GetEventFlag(index);
    public void SetFlag(int index, bool value) => _flags!.SetEventFlag(index, value);
    public int GetWork(int index) => _getWork!(index);
    public void SetWork(int index, int value) => _setWork!(index, value);

    private (IReadOnlyList<GameFlag>, IReadOnlyList<GameWork>) LoadLabels()
    {
        if (_flags is null)
            return ([], []);
        Dictionary<int, NamedEventValue> flagNames = [];
        Dictionary<int, NamedEventWork> workNames = [];
        if (GetLabelSuffix(_sav.Version) is { } game)
        {
            try
            {
                var labels = new EventLabelCollection(game, FlagCount, WorkCount);
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
        var flags = Enumerable.Range(0, FlagCount)
            .Select(i => flagNames.TryGetValue(i, out var n) ? new GameFlag(i, n.Name, n.Type) : new GameFlag(i, "", NamedEventType.None)).ToList();
        var works = Enumerable.Range(0, WorkCount)
            .Select(i => workNames.TryGetValue(i, out var n) ? new GameWork(i, n.Name, n.Type, n.PredefinedValues) : new GameWork(i, "", NamedEventType.None, [])).ToList();
        return (flags, works);
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
