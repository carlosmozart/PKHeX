using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    public bool HasFrontier => _sav is SAV3E or SAV4;
    public bool IsFrontierTab => Tab == 11;
    public FrontierEditorViewModel? Frontier { get; private set; }
    private void RefreshFrontier()
    {
        Frontier = HasFrontier ? new(_sav!, Edit) : null;
        Raise(nameof(Frontier)); Raise(nameof(HasFrontier));
    }
}

/// <summary>
/// Battle Frontier de Emerald (Gen 3) e de Platinum/HeartGold/SoulSilver (Gen 4; em Diamond/Pearl, so a Battle Tower):
/// BP, Frontier Pass e simbolos (Gen 3), impressoes (Gen 4) e sequencias de cada instalacao. Igual ao SAV_Misc3/SAV_Misc4 do PKHeX.
/// </summary>
public sealed class FrontierEditorViewModel : ViewModelBase
{
    private readonly SaveFile _sav;
    private readonly Action<string, Action> _edit;
    private readonly Gen4Layout? _g4;

    // Gen 4: endereco de cada instalacao no bloco geral (como o BFF do SAV_Misc4).
    private sealed record Gen4Facility(int Values, int Modes, int Address, int Stride, int FlagAddress);
    private sealed record Gen4Layout(Gen4Facility[] Facilities, int PrintWork, int TowerContinueOffset);
    // Qual campo da tela recebe cada u16 da instalacao (-1 = ignorado). 0 = atual, 1 = trocas/CP atual, 2 = recorde, 3 = trocas/CP recorde.
    private static readonly int[][] Gen4Values = [[2, 0], [2, 0, 3, 1], [2, 0, 1, -1, 3]];
    private static readonly string[][] Gen4Modes = [["Singles", "Doubles", "Multi"], ["Singles", "Doubles", "Multi (Trainer)", "Multi (Friend)", "Wi-Fi"]];

    public FrontierEditorViewModel(SaveFile sav, Action<string, Action> edit)
    {
        _sav = sav; _edit = edit;
        _g4 = sav switch
        {
            SAV4DP => new([new(0, 1, 0x5FCA, 0x04, 0x6601)], -1, 3),
            SAV4Pt => new([new(0, 1, 0x68E0, 0x04, 0x723D), new(1, 0, 0x68F4, 0x10, 0x7EF8), new(0, 0, 0x6924, 0x18, 0x7EFC), new(2, 0, 0x696C, 0x10, 0x7F00), new(0, 0, 0x699C, 0x04, 0x7F04)], 79, 1),
            SAV4HGSS => new([new(0, 1, 0x5264, 0x04, 0x5BC1), new(1, 0, 0x5278, 0x10, 0x687C), new(0, 0, 0x52A8, 0x18, 0x6880), new(2, 0, 0x52F0, 0x10, 0x6884), new(0, 0, 0x5320, 0x04, 0x6888)], 77, 1),
            _ => null,
        };
        Facilities = IsGen3
            ? ["Battle Tower", "Battle Dome", "Battle Palace", "Battle Arena", "Battle Factory", "Battle Pike", "Battle Pyramid"]
            : [.. new[] { "Battle Tower", "Battle Factory", "Battle Hall", "Battle Castle", "Battle Arcade" }.Take(_g4!.Facilities.Length)];
        if (IsGen3)
            Symbols = [.. Facilities.Select((name, i) => new FrontierSymbolViewModel(name, (BattleFrontierFacility3)i, (SAV3)sav, edit))];
        if (_g4 is { PrintWork: >= 0 } g)
            Prints = [.. Facilities.Select((name, i) => new FrontierPrintViewModel(name, (SAV4)sav, g.PrintWork + i, edit))];
        LoadFacility();
    }

    public bool IsGen3 => _sav is SAV3E;
    public bool IsGen4 => _g4 is not null;
    public string Note => IsGen3
        ? "Emerald: BP, Frontier Pass, símbolos e sequências de cada instalação. As mudanças valem na hora; Salvar grava o arquivo."
        : _sav is SAV4DP ? "Diamond/Pearl só têm a Battle Tower. As mudanças valem na hora; Salvar grava o arquivo."
        : "Platinum/HeartGold/SoulSilver: BP, impressões e sequências de cada instalação. As mudanças valem na hora; Salvar grava o arquivo.";

    public int BP
    {
        get => _sav switch { SAV3E e => e.SmallBlock.BP, SAV4 s => s.BP, _ => 0 };
        set
        {
            value = Math.Clamp(value, 0, 9999);
            if (value == BP) return;
            _edit("Battle Points", () => { if (_sav is SAV3E e) e.SmallBlock.BP = (ushort)value; else if (_sav is SAV4 s) s.BP = value; });
            Raise();
        }
    }

    public bool FrontierPass
    {
        get => IsGen3 && ((SAV3)_sav).GetEventFlag(BattleFrontier3.FrontierPassFlagIndex);
        set { if (!IsGen3 || value == FrontierPass) return; _edit("Frontier Pass", () => ((SAV3)_sav).SetEventFlag(BattleFrontier3.FrontierPassFlagIndex, value)); Raise(); }
    }

    public IReadOnlyList<FrontierSymbolViewModel> Symbols { get; } = [];
    public IReadOnlyList<FrontierPrintViewModel> Prints { get; } = [];
    public bool HasPrints => Prints.Count > 0;
    public RelayCommand AllGoldCommand => new(() =>
    {
        _edit(IsGen3 ? "Todos os símbolos de ouro" : "Todas as impressões de ouro", () =>
        {
            foreach (var s in Symbols) s.Write(2);
            foreach (var p in Prints) p.Write((int)BattleFrontierPrintStatus4.SecondReceived);
        });
        foreach (var s in Symbols) s.Refresh();
        foreach (var p in Prints) p.Refresh();
    });

    // Sequencias
    public IReadOnlyList<string> Facilities { get; }
    private int _facility;
    public int FacilityIndex { get => _facility; set { if (value < 0 || !Set(ref _facility, value)) return; _mode = 0; _level = 0; LoadFacility(); } }
    public IReadOnlyList<string> Modes { get; private set; } = [];
    private int _mode;
    public int ModeIndex { get => _mode; set { if (value < 0 || !Set(ref _mode, value)) return; LoadStats(); } }
    public bool HasModes => Modes.Count > 1;
    public IReadOnlyList<string> Levels { get; } = ["Level 50", "Open Level"];
    private int _level;
    public int LevelIndex { get => _level; set { if (value < 0 || !Set(ref _level, value)) return; LoadStats(); } }
    /// <summary>Na Gen 3 todas as instalacoes separam Level 50 e Open Level; na Gen 4, so a Battle Factory.</summary>
    public bool HasLevels => IsGen3 || _facility == 1;
    public IReadOnlyList<FrontierStatViewModel> Stats { get; private set; } = [];

    private void LoadFacility()
    {
        if (IsGen3)
        {
            var count = BattleFrontier3.GetModeCount((BattleFrontierFacility3)_facility);
            Modes = [.. new[] { "Singles", "Doubles", "Multi", "Linked Multi" }.Take(count)];
        }
        else
        {
            Modes = Gen4Modes[_g4!.Facilities[_facility].Modes];
        }
        foreach (var p in (string[])[nameof(Modes), nameof(HasModes), nameof(ModeIndex), nameof(LevelIndex), nameof(HasLevels)]) Raise(p);
        LoadStats();
    }

    private void LoadStats()
    {
        if (IsGen3)
        {
            var facility = (BattleFrontierFacility3)_facility;
            Stats = [.. BattleFrontier3.GetValidStats(facility).Select(stat => new FrontierStatViewModel(Gen3Label(facility, stat), 9999,
                () => Gen3().GetStat(facility, (BattleFrontierBattleMode3)_mode, (BattleFrontierRecordType3)_level, stat),
                v => _edit("Sequência do Battle Frontier", () => Gen3().SetStat(facility, (BattleFrontierBattleMode3)_mode, (BattleFrontierRecordType3)_level, stat, (ushort)v))))];
        }
        else
        {
            var f = _g4!.Facilities[_facility];
            var map = Gen4Values[f.Values];
            var list = new List<FrontierStatViewModel>();
            for (int field = 0; field < 4; field++)
            {
                int slot = Array.IndexOf(map, field);
                if (slot < 0) continue;
                int s = slot, which = field;
                list.Add(new(Gen4Label(which), 9999, () => ReadUInt16LittleEndian(General[(StatAddress + (s * 2))..]),
                    v => _edit("Sequência do Battle Frontier", () =>
                    {
                        WriteUInt16LittleEndian(General[(StatAddress + (s * 2))..], (ushort)v);
                        if (_facility == 0 && which == 0) // Battle Tower: o jogo guarda tambem quantas series de 7 foram vencidas
                            WriteUInt16LittleEndian(General[(f.FlagAddress + _g4.TowerContinueOffset + (_mode * 2))..], (ushort)(v / 7));
                    })));
            }
            Stats = list;
        }
        Raise(nameof(Stats)); Raise(nameof(Continue)); Raise(nameof(HasLevels));
    }

    /// <summary>"Continuar": a serie esta em andamento (o jogo oferece continuar de onde parou).</summary>
    public bool Continue
    {
        get
        {
            if (IsGen3) return Gen3().GetContinueFlag((BattleFrontierFacility3)_facility, (BattleFrontierBattleMode3)_mode, (BattleFrontierRecordType3)_level);
            var f = _g4!.Facilities[_facility];
            return (General[f.FlagAddress] & ContinueMask) != 0;
        }
        set
        {
            if (value == Continue) return;
            _edit("Série em andamento", () =>
            {
                if (IsGen3) { var bf = Gen3(); bf.SetContinueFlag((BattleFrontierFacility3)_facility, (BattleFrontierBattleMode3)_mode, (BattleFrontierRecordType3)_level, value); return; }
                var f = _g4!.Facilities[_facility];
                if (value)
                {
                    General[f.FlagAddress] |= ContinueMask;
                    if (_facility == 3) General[f.FlagAddress + 1] |= 0x01; // Battle Castle: o PKHeX liga este bit junto
                }
                else General[f.FlagAddress] &= (byte)~ContinueMask;
            });
            Raise();
        }
    }

    private BattleFrontier3 Gen3() => ((SAV3E)_sav).SmallBlock.BattleFrontier;
    private Span<byte> General => ((SAV4)_sav).General;
    private int RecordLevel => HasLevels && !IsGen3 ? _level : 0;
    private int StatAddress { get { var f = _g4!.Facilities[_facility]; return f.Address + (f.Stride * _mode) + (RecordLevel << 3); } }
    private byte ContinueMask => (byte)(1 << (_mode + (RecordLevel << 2)));

    private static string Gen3Label(BattleFrontierFacility3 facility, BattleFrontierStatType3 stat) => (facility, stat) switch
    {
        (_, BattleFrontierStatType3.CurrentStreak) => "Sequência atual",
        (_, BattleFrontierStatType3.RecordStreak) => "Recorde",
        (BattleFrontierFacility3.Dome, BattleFrontierStatType3.Championships) => "Campeonatos",
        (BattleFrontierFacility3.Pike, BattleFrontierStatType3.RecordCleared) => "Vezes concluído",
        (BattleFrontierFacility3.Factory, BattleFrontierStatType3.CurrentSwapped) => "Trocas (atual)",
        (BattleFrontierFacility3.Factory, BattleFrontierStatType3.RecordSwapped) => "Trocas (recorde)",
        _ => stat.ToString(),
    };

    private string Gen4Label(int field) => (field, _facility) switch
    {
        (0, _) => "Sequência atual",
        (2, _) => "Recorde",
        (1, 1) => "Trocas (atual)",
        (3, 1) => "Trocas (recorde)",
        (1, 3) => "CP (atual)",
        (3, 3) => "CP (recorde)",
        _ => "",
    };
}

/// <summary>Um numero de sequencia (le e grava pelo jogo a cada mudanca).</summary>
public sealed class FrontierStatViewModel(string label, int max, Func<int> get, Action<int> set) : ViewModelBase
{
    public string Label => label;
    public int Max => max;
    public int Value
    {
        get => Math.Min(get(), max);
        set { value = Math.Clamp(value, 0, max); if (value == get()) return; set(value); Raise(); }
    }
}

/// <summary>Simbolo de uma instalacao da Gen 3: nenhum, prata ou ouro (duas flags de evento).</summary>
public sealed class FrontierSymbolViewModel(string name, BattleFrontierFacility3 facility, SAV3 sav, Action<string, Action> edit) : ViewModelBase
{
    public string Name => name;
    public IReadOnlyList<string> Options { get; } = ["Nenhum", "Prata", "Ouro"];
    public int State
    {
        get => sav.GetEventFlag(BattleFrontier3.GetSymbolSilverFlagIndex(facility)) ? sav.GetEventFlag(BattleFrontier3.GetSymbolGoldFlagIndex(facility)) ? 2 : 1 : 0;
        set { if (value is < 0 or > 2 || value == State) return; edit($"Símbolo da {name}", () => Write(value)); Raise(); }
    }
    internal void Write(int value)
    {
        sav.SetEventFlag(BattleFrontier3.GetSymbolSilverFlagIndex(facility), value > 0);
        sav.SetEventFlag(BattleFrontier3.GetSymbolGoldFlagIndex(facility), value == 2);
    }
    internal void Refresh() => Raise(nameof(State));
}

/// <summary>Impressao de uma instalacao da Gen 4 (valor de evento): nenhuma, prata/ouro pronta para receber ou recebida.</summary>
public sealed class FrontierPrintViewModel(string name, SAV4 sav, int work, Action<string, Action> edit) : ViewModelBase
{
    public string Name => name;
    public IReadOnlyList<string> Options { get; } = ["Nenhuma", "Prata pronta", "Prata recebida", "Ouro pronta", "Ouro recebida"];
    public int State
    {
        get => Math.Min((int)sav.GetWork(work), 4);
        set { if (value is < 0 or > 4 || value == State) return; edit($"Impressão da {name}", () => Write(value)); Raise(); }
    }
    internal void Write(int value) => sav.SetWork(work, (ushort)value);
    internal void Refresh() => Raise(nameof(State));
}
