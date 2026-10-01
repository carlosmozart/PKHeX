using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Editor de um Pokemon. Trabalha sobre uma copia (Clone) e so grava no save ao clicar em "Aplicar".
/// Para adicionar um novo campo: crie uma propriedade aqui que leia/escreva em <see cref="_pk"/> e um controle na view.
/// </summary>
public sealed class PokemonEditorViewModel : ViewModelBase
{
    private PKM _pk;
    private readonly SaveFile? _sav;
    private byte[] _savedData;
    /// <summary>Ha edicoes ainda nao aplicadas no slot.</summary>
    public bool IsModified => !_pk.Data.SequenceEqual(_savedData);
    private readonly Action<PKM> _apply;
    private readonly Action<string> _status;

    /// <param name="pendingApply">Pokemon que veio de fora (banco de encontros/eventos): ja conta como edicao nao aplicada.</param>
    public PokemonEditorViewModel(PKM source, string location, Action<PKM> apply, Action<string> status, bool isNew = false, bool pendingApply = false, SaveFile? sav = null)
    {
        _sav = sav;
        _isNew = isNew;
        _pk = source.Clone();
        _savedData = pendingApply ? [] : _pk.Data.ToArray();
        _apply = apply;
        _status = status;
        Location = location;
        Stats =
        [
            new("PS",   "#5FD068", () => _pk.IV_HP,  v => _pk.IV_HP = v,  () => _pk.EV_HP,  v => _pk.EV_HP = v,  _pk.MaxIV, _pk.MaxEV),
            new("Atq",  "#F5A524", () => _pk.IV_ATK, v => _pk.IV_ATK = v, () => _pk.EV_ATK, v => _pk.EV_ATK = v, _pk.MaxIV, _pk.MaxEV),
            new("Def",  "#F2D44E", () => _pk.IV_DEF, v => _pk.IV_DEF = v, () => _pk.EV_DEF, v => _pk.EV_DEF = v, _pk.MaxIV, _pk.MaxEV),
            new("AtE",  "#4FA3F7", () => _pk.IV_SPA, v => _pk.IV_SPA = v, () => _pk.EV_SPA, v => _pk.EV_SPA = v, _pk.MaxIV, _pk.MaxEV),
            new("DeE",  "#8C7CF0", () => _pk.IV_SPD, v => _pk.IV_SPD = v, () => _pk.EV_SPD, v => _pk.EV_SPD = v, _pk.MaxIV, _pk.MaxEV),
            new("Vel",  "#F06292", () => _pk.IV_SPE, v => _pk.IV_SPE = v, () => _pk.EV_SPE, v => _pk.EV_SPE = v, _pk.MaxIV, _pk.MaxEV),
        ];
        foreach (var s in Stats)
            s.Changed += Refresh;
        ApplyCommand = new RelayCommand(() => { _apply(_pk.Clone()); _savedData = _pk.Data.ToArray(); });
        MaxIVsCommand = new RelayCommand(() => { foreach (var s in Stats) s.IV = _pk.MaxIV; });
        ClearEVsCommand = new RelayCommand(() => { foreach (var s in Stats) s.EV = 0; });
        MakeShinyCommand = new RelayCommand(() => { _pk.SetShiny(); RaiseAll(); });
        LegalizeCommand = new RelayCommand(() => _ = LegalizeAsync(), () => !IsLegalizing);
        SuggestMovesCommand = new RelayCommand(() => Fix("Golpes sugeridos", pk => CoreAdapter.SuggestMoves(pk)));
        SuggestRelearnCommand = new RelayCommand(() => Fix("Golpes de reaprender", pk => CoreAdapter.SuggestRelearnMoves(pk)));
        SuggestMetCommand = new RelayCommand(() => Fix("Encontro sugerido", CoreAdapter.SuggestMetData, "nenhum encontro possível para esta espécie neste jogo"));
        FixIVsCommand = new RelayCommand(() => { foreach (var s in Stats) s.IV = _pk.MaxIV; _status(LegalityStatus("IVs máximos: aplicado")); });
        BallList = CoreAdapter.GetBalls();
        MetLocationList = CoreAdapter.GetMetLocations(_pk);
        Refresh();
    }

    public IReadOnlyList<string> SpeciesList => CoreAdapter.SpeciesNames;
    public IReadOnlyList<string> MoveList => CoreAdapter.MoveNames;
    public IReadOnlyList<string> ItemList => CoreAdapter.ItemNames;
    public IReadOnlyList<string> NatureList => CoreAdapter.NatureNames;
    public IReadOnlyList<StatViewModel> Stats { get; }
    public string Location { get; }
    public int MaxIV => _pk.MaxIV;
    public int MaxEV => _pk.MaxEV;

    public RelayCommand ApplyCommand { get; }
    public RelayCommand MaxIVsCommand { get; }
    public RelayCommand ClearEVsCommand { get; }
    public RelayCommand MakeShinyCommand { get; }
    public RelayCommand SuggestMovesCommand { get; }
    public RelayCommand LegalizeCommand { get; }

    private bool _isLegalizing;
    public bool IsLegalizing { get => _isLegalizing; private set { Set(ref _isLegalizing, value); LegalizeCommand.NotifyCanExecuteChanged(); } }

    /// <summary>
    /// Legalizar: gera de novo a partir de um encontro real do jogo (PID/IV corretos) mantendo natureza, nivel, item,
    /// apelido e golpes quando possivel. Roda em segundo plano (na Gen 3/4 a busca de PID pode levar alguns segundos).
    /// </summary>
    private async Task LegalizeAsync()
    {
        if (_sav is null)
        {
            _status("Legalizar: indisponível.");
            return;
        }
        IsLegalizing = true;
        _status($"Legalizando {SpeciesName}...");
        try
        {
            var current = _pk.Clone();
            var sav = _sav;
            var (result, message) = await Task.Run(() => (EncounterDatabase.Legalize(sav, current, out var m), m));
            if (result is null)
            {
                _status($"Legalizar: {message}.");
                return;
            }
            _pk = result;
            _isNew = false;
            RaiseAll();
            _status($"Legalizado a partir de: {message}. Confira e clique em Aplicar para gravar.");
        }
        catch (Exception ex)
        {
            _status($"Legalizar: erro ({ex.Message})");
        }
        finally
        {
            IsLegalizing = false;
        }
    }
    public RelayCommand SuggestRelearnCommand { get; }
    public RelayCommand SuggestMetCommand { get; }
    public RelayCommand FixIVsCommand { get; }
    /// <summary>Golpes de reaprender so existem a partir da Gen 6.</summary>
    public bool HasRelearnMoves => _pk.Format >= 6;
    public IReadOnlyList<string> LegalityIssues { get; private set; } = [];
    public bool HasLegalityIssues => ShowIllegal && LegalityIssues.Count > 0;

    /// <summary>Aplica uma correcao sugerida e informa o resultado na barra de status.</summary>
    private void Fix(string what, Func<PKM, bool?> apply, string? whenNull = null)
    {
        bool? changed;
        try { changed = apply(_pk); }
        catch (Exception ex) { _status($"{what}: erro ({ex.Message})"); return; }
        if (changed is null)
        {
            _status($"{what}: {whenNull ?? "indisponível"}.");
            return;
        }
        if (changed == false)
        {
            _status($"{what}: nada a mudar.");
            return;
        }
        _isNew = false;
        RaiseAll();
        _status(LegalityStatus($"{what}: aplicado"));
    }

    private string LegalityStatus(string done) => $"{done}. Agora: {(IsLegal ? "legal ✓" : "ainda ilegal ⚠")}. Clique em Aplicar para gravar.";

    /// <summary>Aba selecionada (mantida pelo MainViewModel ao trocar de slot).</summary>
    public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }
    private int _selectedTab;
    public IReadOnlyList<string> StatLabels { get; } = ["PS", "Atq", "Def", "AtE", "DeE", "Vel"];
    public IReadOnlyList<double> RadarValues { get; private set; } = [0, 0, 0, 0, 0, 0];
    public IReadOnlyList<TypeChip> Types { get; private set; } = [];
    public string GenderSymbol => CoreAdapter.GetGenderSymbol(_pk);
    public string SpeciesName => (uint)_pk.Species < (uint)SpeciesList.Count ? SpeciesList[_pk.Species] : "";
    public int StatTotal { get; private set; }
    public int EVTotal { get; private set; }
    public int IVTotal { get; private set; }
    public string EVSummary => $"EVs {EVTotal}/510";
    public string IVSummary => $"IVs {IVTotal}/{_pk.MaxIV * 6}";

    public string Nickname { get => _pk.Nickname; set { _pk.Nickname = value; Refresh(); } }
    public int Species { get => _pk.Species; set { if (value >= 0 && value != _pk.Species) { CoreAdapter.ChangeSpecies(_pk, (ushort)value); _isNew = false; RaiseAll(); } } }
    public int Level { get => _pk.CurrentLevel; set { _pk.CurrentLevel = (byte)Math.Clamp(value, 1, 100); Refresh(); } }
    public int Nature { get => (int)_pk.StatAlignment; set { if (value >= 0 && value != (int)_pk.StatAlignment) { _pk.SetNature((Nature)value); Refresh(); } } }
    public int HeldItem { get => _pk.HeldItem; set { if (value >= 0) { _pk.HeldItem = value; Refresh(); } } }
    public int Move1 { get => _pk.Move1; set { if (value >= 0) { _pk.Move1 = (ushort)value; Refresh(); } } }
    public int Move2 { get => _pk.Move2; set { if (value >= 0) { _pk.Move2 = (ushort)value; Refresh(); } } }
    public int Move3 { get => _pk.Move3; set { if (value >= 0) { _pk.Move3 = (ushort)value; Refresh(); } } }
    public int Move4 { get => _pk.Move4; set { if (value >= 0) { _pk.Move4 = (ushort)value; Refresh(); } } }

    // Encontro
    public IReadOnlyList<ComboItem> BallList { get; }
    public IReadOnlyList<ComboItem> MetLocationList { get; }
    public ComboItem? Ball { get => Find(BallList, _pk.Ball); set { if (value is not null) { _pk.Ball = (byte)value.Value; Refresh(); } } }
    public ComboItem? MetLocation { get => Find(MetLocationList, _pk.MetLocation); set { if (value is not null) { _pk.MetLocation = (ushort)value.Value; Refresh(); } } }
    public int MetLevel { get => _pk.MetLevel; set { _pk.MetLevel = (byte)Math.Clamp(value, 0, 100); Refresh(); } }
    public bool HasMetDate => _pk.MetDate is not null;
    public DateTime? MetDate
    {
        get => _pk.MetDate?.ToDateTime(TimeOnly.MinValue);
        set { if (value is { } v) { _pk.MetDate = DateOnly.FromDateTime(v); Refresh(); } }
    }
    public string OriginGame => CoreAdapter.GetVersionName(_pk.Version);

    // Treinador
    public string TrainerName { get => _pk.OriginalTrainerName; set { _pk.OriginalTrainerName = value; Refresh(); } }
    public int TID { get => _pk.TID16; set { _pk.TID16 = (ushort)Math.Clamp(value, 0, ushort.MaxValue); Refresh(); } }
    public int SID { get => _pk.SID16; set { _pk.SID16 = (ushort)Math.Clamp(value, 0, ushort.MaxValue); Refresh(); } }
    public bool TrainerIsFemale { get => _pk.OriginalTrainerGender == 1; set { _pk.OriginalTrainerGender = (byte)(value ? 1 : 0); Refresh(); } }

    // Extras
    public int Friendship { get => _pk.CurrentFriendship; set { _pk.CurrentFriendship = (byte)Math.Clamp(value, 0, 255); Refresh(); } }
    public string PID => $"{_pk.PID:X8}";
    public string EncryptionConstant => $"{_pk.EncryptionConstant:X8}";

    private static ComboItem? Find(IReadOnlyList<ComboItem> list, int value)
    {
        foreach (var item in list)
            if (item.Value == value)
                return item;
        return null;
    }

    private void RaiseAll()
    {
        Refresh();
        Raise(string.Empty);
        foreach (var st in Stats)
            st.RaiseAll();
    }

    public string AbilityName => (uint)_pk.Ability < CoreAdapter.AbilityNames.Count ? CoreAdapter.AbilityNames[_pk.Ability] : "?";
    public bool IsShiny => _pk.IsShiny;
    public Bitmap? Sprite { get; private set; }
    public bool IsLegal { get; private set; }
    private bool _isNew;
    /// <summary>Pokemon novo (slot vazio) ainda sem especie escolhida: o selo de legalidade fica oculto.</summary>
    public bool ShowLegality => !_isNew;
    public bool ShowLegal => ShowLegality && IsLegal;
    public bool ShowIllegal => ShowLegality && !IsLegal;
    public string LegalityText { get; private set; } = "";
    public string LegalityReport { get; private set; } = "";

    public string ExportShowdown() => CoreAdapter.ToShowdown(_pk);

    public void ImportShowdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _status("Área de transferência vazia.");
            return;
        }
        var error = CoreAdapter.ApplyShowdown(_pk, text);
        if (error is null)
            _isNew = false;
        RaiseAll(); // atualiza todos os campos
        _status(error ?? "Set Showdown importado. Clique em Aplicar para gravar.");
    }

    private void Refresh()
    {
        Sprite = SpriteService.GetSprite(_pk);
        var final = CoreAdapter.GetFinalStats(_pk);
        var bases = CoreAdapter.GetBaseStats(_pk);
        var mods = CoreAdapter.GetNatureModifiers(_pk);
        for (int i = 0; i < Stats.Count; i++)
            Stats[i].Update(final[i], bases[i], mods[i]);
        // Escala do radar: maior atributo = borda; minimo 100 para nao exagerar em niveis baixos
        double max = Math.Max(100, Math.Max(final[0], Math.Max(final[1], Math.Max(final[2], Math.Max(final[3], Math.Max(final[4], final[5]))))));
        RadarValues = [.. Array.ConvertAll(final, v => v / max)];
        StatTotal = 0; EVTotal = 0; IVTotal = 0;
        foreach (var st in Stats) { StatTotal += st.Total; EVTotal += st.EV; IVTotal += st.IV; }
        Types = [.. System.Linq.Enumerable.Select(CoreAdapter.GetTypes(_pk), t => new TypeChip(t.Name, t.Argb))];
        foreach (var p in (string[])[nameof(RadarValues), nameof(StatTotal), nameof(EVTotal), nameof(IVTotal), nameof(EVSummary), nameof(IVSummary), nameof(Types), nameof(GenderSymbol), nameof(SpeciesName)])
            Raise(p);
        (IsLegal, LegalityReport) = CoreAdapter.CheckLegality(_pk);
        LegalityText = IsLegal ? "Legal" : "Ilegal";
        LegalityIssues = IsLegal ? [] : CoreAdapter.GetLegalityIssues(_pk);
        foreach (var p in (string[])[nameof(Sprite), nameof(IsLegal), nameof(ShowLegality), nameof(ShowLegal), nameof(ShowIllegal), nameof(LegalityIssues), nameof(HasLegalityIssues), nameof(LegalityText), nameof(LegalityReport), nameof(AbilityName), nameof(IsShiny), nameof(PID), nameof(EncryptionConstant)])
            Raise(p);
    }
}

public sealed record TypeChip(string Name, uint Argb)
{
    public Avalonia.Media.IBrush Brush { get; } = new Avalonia.Media.SolidColorBrush(Argb);
}

public sealed class StatViewModel(string name, string color, Func<int> getIV, Action<int> setIV, Func<int> getEV, Action<int> setEV, int maxIV, int maxEV) : ViewModelBase
{
    public event Action? Changed;
    public void RaiseAll() => Raise(string.Empty);
    public string Name { get; } = name;
    public Avalonia.Media.IBrush Color { get; } = Avalonia.Media.Brush.Parse(color);
    public int MaxIV { get; } = maxIV;
    public int MaxEV { get; } = Math.Min(maxEV, 252);
    public int IV { get => getIV(); set { setIV(Math.Clamp(value, 0, MaxIV)); Raise(); Changed?.Invoke(); } }
    public int EV { get => getEV(); set { setEV(Math.Clamp(value, 0, MaxEV)); Raise(); Changed?.Invoke(); } }

    public int Total { get; private set; }
    public int Base { get; private set; }
    /// <summary>+1 natureza aumenta, -1 diminui.</summary>
    public int NatureMod { get; private set; }
    public bool IsBoosted => NatureMod > 0;
    public bool IsHindered => NatureMod < 0;
    public string NatureArrow => NatureMod switch { > 0 => "▲", < 0 => "▼", _ => "" };
    public double BaseRatio => Math.Min(1, Base / 180.0);

    public void Update(int total, int baseStat, int mod)
    {
        Total = total; Base = baseStat; NatureMod = mod;
        foreach (var p in (string[])[nameof(Total), nameof(Base), nameof(NatureMod), nameof(IsBoosted), nameof(IsHindered), nameof(NatureArrow), nameof(BaseRatio)])
            Raise(p);
    }
}
