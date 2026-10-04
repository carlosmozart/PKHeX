using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    public Func<bool> IsLegalMode { get; set; } = () => true;
    public bool HasRoamers => _sav is SAV3RS or SAV3E or SAV3FRLG or SAV6XY;
    public bool IsRoamersTab => Tab == 8;
    public RoamerEditorViewModel? Roamer { get; private set; }
    private void RefreshRoamers()
    {
        Roamer = HasRoamers ? new(_sav!, Edit, _status, () => IsLegalMode()) : null;
        Raise(nameof(Roamer)); Raise(nameof(HasRoamers));
    }
}

public sealed class RoamerEditorViewModel : ViewModelBase
{
    private readonly SaveFile _sav;
    private readonly Action<string, Action> _edit;
    private readonly Action<string> _status;
    private readonly Func<bool> _legal;
    private readonly Roamer3? _draft;
    private readonly Roamer6? _xy;
    private readonly IReadOnlyList<EncounterStatic3> _encounters;
    public bool IsGen3 => _draft is not null;
    public bool IsXY => _xy is not null;
    public bool HasIvBug => _draft?.IsGlitched == true;
    public const string Explanation = "Na Gen 3, edite a cópia e use Aplicar errante. Gerar dados legais usa um encontro real e substitui PID/IVs. Fazer reaparecer reativa o errante; em X/Y, reinicia os encontros. O Core não expõe o local atual para edição. Salvar grava o arquivo.";
    public string Note => Explanation;
    public string IvBugNote => "Bug do jogo em Ruby/Sapphire/FireRed/LeafGreen: na captura só são carregados 8 bits dos IVs (HP e parte do Attack); os outros IVs viram zero. Emerald não tem esse bug.";
    public IReadOnlyList<string> SpeciesOptions { get; }
    public IReadOnlyList<string> States { get; } = ["Não começou", "Vagando", "Estacionário", "Derrotado", "Capturado"];
    public IReadOnlyList<RoamerIvViewModel> IvRows { get; }
    public RoamerEditorViewModel(SaveFile sav, Action<string, Action> edit, Action<string> status, Func<bool> legal)
    {
        _sav = sav; _edit = edit; _status = status; _legal = legal; _encounters = [];
        if (sav is SAV3 s3)
        {
            _draft = new(s3.LargeBlock.RoamerData.ToArray(), sav is not SAV3E);
            ushort[] candidates = sav is SAV3FRLG ? [243, 244, 245] : [380, 381];
            _encounters = candidates.SelectMany(sp => EncounterDatabase.SearchEncounters(sav, sp, true)).OfType<EncounterStatic3>()
                .Where(e => e.IsRoaming && (e.Version.Contains(sav.Version) || sav.Version.Contains(e.Version))).ToArray();
            SpeciesOptions = (_legal() ? _encounters.Select(e => GameInfo.Strings.Species[e.Species]).Distinct() : GameInfo.Strings.Species.Skip(1).Take(sav.MaxSpeciesID)).ToArray();
            if (_draft.Species == 0 && _encounters.FirstOrDefault() is { } first) { _draft.Species = first.Species; _draft.CurrentLevel = first.LevelMin; }
            IvRows = new[] { "HP", "Attack", "Defense", "Sp. Atk", "Sp. Def", "Speed" }.Select((name, i) => new RoamerIvViewModel(name, i, _draft, () => Raise(nameof(CapturedIvs)))).ToArray();
        }
        else
        {
            _xy = ((SAV6XY)sav).Encount.Roamer; SpeciesOptions = ["Articuno", "Zapdos", "Moltres"]; IvRows = [];
        }
    }
    public string SpeciesName
    {
        get => GameInfo.Strings.Species[_draft?.Species ?? (_xy!.Species == 0 ? ExpectedBird : _xy.Species)];
        set
        {
            int species = Array.IndexOf(GameInfo.Strings.specieslist, value); if (species <= 0 || species > _sav.MaxSpeciesID) return;
            if (_draft is { } draft) { draft.Species = (ushort)species; Raise(); return; }
            if (_legal() && species != ExpectedBird) { _status("Modo legal: a ave errante deve corresponder ao inicial escolhido em X/Y."); Raise(); return; }
            if (species is >= 144 and <= 146) _edit("Espécie do errante", () => _xy!.Species = (ushort)species); Raise();
        }
    }
    private ushort ExpectedBird => (ushort)(144 + Math.Clamp((int)((SAV6XY)_sav).EventWork.GetWork(48), 0, 2));
    public int Level { get => _draft?.CurrentLevel ?? 0; set { if (_draft is { } d && value is >= 1 and <= 100) { d.CurrentLevel = (byte)value; Raise(); } } }
    public int Hp { get => _draft?.HP_Current ?? 0; set { if (_draft is { } d && value is >= 0 and <= ushort.MaxValue) { d.HP_Current = (ushort)value; Raise(); } } }
    public bool Active { get => _draft?.IsActive == true; set { if (_draft is { } d) { d.IsActive = value; Raise(); } } }
    public string Pid { get => _draft?.PID.ToString("X8") ?? ""; set { if (_draft is { } d && uint.TryParse(value.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) { d.PID = n; Raise(); } } }
    public string CapturedIvs => _draft is null ? "" : "IVs na captura: " + string.Join(" / ", HasIvBug ? _draft.IVsGlitch : _draft.IVs);
    public int StateIndex { get => (int)(_xy?.RoamStatus ?? 0); set { if (_xy is { } r && value is >= 0 and <= 4) { if (_legal() && r.Species != 0 && r.Species != ExpectedBird) { _status("Modo legal: a ave errante deve corresponder ao inicial escolhido em X/Y."); Raise(); return; } _edit("Estado do errante", () => { if (r.Species == 0) r.Species = ExpectedBird; r.RoamStatus = (Roamer6State)value; }); Raise(); } } }
    public uint TimesEncountered { get => _xy?.TimesEncountered ?? 0; set { if (_xy is { } r) _edit("Encontros do errante", () => r.TimesEncountered = value); Raise(); } }
    public RelayCommand ApplyCommand => new(Apply);
    public RelayCommand GenerateCommand => new(Generate);
    public RelayCommand ReappearCommand => new(() =>
    {
        if (_draft is { } draft) { if (draft.PID == 0 && _legal()) Generate(); draft.IsActive = true; Apply(); }
        else _edit("Fazer errante reaparecer", () => { _xy!.Species = ExpectedBird; _xy.RoamStatus = Roamer6State.Roaming; _xy.TimesEncountered = 0; });
        Raise(string.Empty);
    });
    private void Generate()
    {
        if (_draft is null) return;
        var encounter = _encounters.FirstOrDefault(e => e.Species == _draft.Species);
        if (encounter is null || EncounterDatabase.ToEntity(_sav, encounter, out _) is not PK3 pk) { _status("Modo legal: espécie ou nível fora do encontro errante deste jogo."); return; }
        _draft.PID = pk.PID; _draft.SetIVs(pk.IVs); _draft.CurrentLevel = encounter.LevelMin; pk.ResetPartyStats(); _draft.HP_Current = (ushort)pk.Stat_HPMax; Raise(string.Empty); foreach (var row in IvRows) row.Refresh();
    }
    private void Apply()
    {
        if (_draft is null || _sav is not SAV3 sav) return;
        if (_legal() && _draft.IsActive)
        {
            var encounter = _encounters.FirstOrDefault(e => e.Species == _draft.Species && e.LevelMin == _draft.CurrentLevel);
            if (encounter is null) { _status("Modo legal: espécie ou nível fora do encontro errante deste jogo."); return; }
            var pk = (PK3)encounter.ConvertToPKM(sav); pk.PID = _draft.PID; pk.IVs = HasIvBug ? _draft.IVsGlitch : _draft.IVs;
            if (encounter.IsCompatible(MethodFinder.Analyze(pk).Type, pk) == RandomCorrelationRating.Mismatch) { _status("Modo legal: PID e IVs do errante não correspondem ao método do jogo. Use Gerar dados legais."); return; }
        }
        _edit("Aplicar errante", () => _draft.Raw.Span.CopyTo(sav.LargeBlock.RoamerData.Span)); _status("Errante atualizado. Salve para gravar.");
    }
}

public sealed class RoamerIvViewModel(string name, int index, Roamer3 draft, Action changed) : ViewModelBase
{
    private static readonly int[] Order = [0, 1, 2, 4, 5, 3];
    public string Name => name;
    public int Value { get => draft.IVs[Order[index]]; set { if (value is < 0 or > 31) return; var ivs = draft.IVs; ivs[Order[index]] = value; draft.SetIVs(ivs); Raise(); changed(); } }
    public void Refresh() => Raise(nameof(Value));
}
