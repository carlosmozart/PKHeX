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
    public PokemonEditorViewModel(PKM source, string location, Action<PKM> apply, Action<string> status, bool isNew = false, bool pendingApply = false, SaveFile? sav = null, bool legalMode = false)
    {
        _sav = sav;
        _legalMode = legalMode;
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
        Moves = [.. Enumerable.Range(0, 4).Select(i => new MoveSlotViewModel(() => _pk, i, () => MoveOptions))];
        foreach (var m in Moves)
            m.Changed += Refresh;
        HealPPCommand = new RelayCommand(() => { _pk.HealPP(); foreach (var m in Moves) m.RaiseAll(); _status("PP restaurado."); });
        ApplyCommand = new RelayCommand(Apply, () => CanApply);
        MaxIVsCommand = new RelayCommand(() => { foreach (var s in Stats) s.IV = _pk.MaxIV; });
        ClearEVsCommand = new RelayCommand(() => { foreach (var s in Stats) s.EV = 0; });
        MakeShinyCommand = new RelayCommand(() => { _pk.SetShiny(); RaiseAll(); });
        LegalizeCommand = new RelayCommand(() => _ = LegalizeAsync(), () => !IsLegalizing);
        SuggestMovesCommand = new RelayCommand(() => Fix("Golpes sugeridos", pk => CoreAdapter.SuggestMoves(pk)));
        SuggestRelearnCommand = new RelayCommand(() => Fix("Golpes de reaprender", pk => CoreAdapter.SuggestRelearnMoves(pk)));
        SuggestMetCommand = new RelayCommand(() => Fix("Encontro sugerido", CoreAdapter.SuggestMetData, "nenhum encontro possível para esta espécie neste jogo"));
        FixIVsCommand = new RelayCommand(() => { foreach (var s in Stats) s.IV = _pk.MaxIV; _status(LegalityStatus("IVs máximos: aplicado")); });
        _allBalls = CoreAdapter.GetBalls();
        MetLocationList = CoreAdapter.GetMetLocations(_pk);
        Refresh();
    }

    public IReadOnlyList<string> SpeciesList => CoreAdapter.SpeciesNames;
    public IReadOnlyList<string> MoveList => CoreAdapter.MoveNames;
    /// <summary>Especies que existem neste formato (para o campo com sugestoes).</summary>
    public IReadOnlyList<string> SpeciesNames => _speciesNames ??= LegalMode && _sav is not null
        ? [.. CoreAdapter.GetSpeciesInGame().Where(s => s.Value > 0 && s.Value <= _pk.MaxSpeciesID).Select(s => s.Text)]
        : CoreAdapter.GetNames(CoreAdapter.SpeciesNames, _pk.MaxSpeciesID);
    private IReadOnlyList<string>? _speciesNames;

    // Modo legal
    private bool _legalMode;
    /// <summary>
    /// Modo legal: listas so com opcoes legais (golpes que aprende, bolas do encontro, especies do jogo),
    /// mudancas que deixariam o Pokemon ilegal sao desfeitas e Aplicar so grava Pokemon legal.
    /// </summary>
    public bool LegalMode
    {
        get => _legalMode;
        set
        {
            if (!Set(ref _legalMode, value))
                return;
            _speciesNames = null;
            _moveOptions = null;
            _ballKey = null;
            Raise(nameof(SpeciesNames));
            Raise(nameof(MoveOptions));
            RefreshBalls();
            foreach (var m in Moves)
                m.RaiseAll();
            RaiseLegalMode();
        }
    }
    /// <summary>Ultima versao legal do Pokemon: para onde o modo legal volta quando uma mudanca o deixa ilegal.</summary>
    private PKM? _lastLegal;
    /// <summary>Troca de especie/Showdown em andamento: o Legalizar decide o resultado, sem desfazer no meio.</summary>
    private bool _guardSuspended;
    private bool _reverting;

    public bool CanApply => !LegalMode || IsLegal;
    /// <summary>Aviso no editor: modo legal ligado e o Pokemon (ja salvo assim) esta ilegal.</summary>
    public bool ShowLegalModeBlock => LegalMode && ShowIllegal;
    public string ApplyTip => CanApply ? "Grava o Pokémon no slot"
        : "Modo legal: este Pokémon está ilegal. Use ✨ Legalizar ou as correções sugeridas (ou desligue o modo legal na barra lateral).";

    private void RaiseLegalMode()
    {
        foreach (var p in (string[])[nameof(CanApply), nameof(ShowLegalModeBlock), nameof(ApplyTip)])
            Raise(p);
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void Apply()
    {
        if (!CanApply)
        {
            _status(ApplyTip);
            return;
        }
        _apply(_pk.Clone());
        _savedData = _pk.Data.ToArray();
    }

    /// <summary>
    /// Modo legal: chamado depois de cada mudanca. Se o Pokemon era legal e deixou de ser, volta para a ultima
    /// versao legal e explica o motivo. Pokemon que ja era ilegal pode ser editado (para corrigir), mas nao aplicado.
    /// </summary>
    private void GuardLegality()
    {
        if (IsLegal)
        {
            _lastLegal = _pk.Clone();
            return;
        }
        if (!LegalMode || _guardSuspended || _reverting || _lastLegal is null || _isNew)
            return;
        var why = LegalityIssues.Count > 0 ? LegalityIssues[0] : "o resultado seria ilegal";
        _reverting = true;
        try
        {
            _pk = _lastLegal.Clone();
            RaiseAll();
        }
        finally
        {
            _reverting = false;
        }
        // O controle que disparou a mudanca ainda mostra o valor novo: atualiza de novo depois do binding terminar.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => RaiseAll());
        _status($"Modo legal: mudança desfeita, deixaria o Pokémon ilegal ({why}).");
    }
    /// <summary>Golpes que existem neste formato (para os campos com sugestoes).</summary>
    public IReadOnlyList<string> MoveNames => _moveNames ??= CoreAdapter.GetNames(CoreAdapter.MoveNames, _pk.MaxMoveID);
    private IReadOnlyList<string>? _moveNames;

    /// <summary>
    /// Opcoes da lista de golpes: primeiro os que o Pokemon aprende oficialmente (fundo verde), depois os demais,
    /// cada grupo em ordem alfabetica. Recalculado so quando especie/forma/nivel/encontro mudam.
    /// </summary>
    public IReadOnlyList<MoveOption> MoveOptions
    {
        get
        {
            var key = (_pk.Species, _pk.Form, _pk.CurrentLevel, _pk.MetLevel, _pk.MetLocation, _pk.Version, _pk.IsEgg, GameInfo.CurrentLanguage, LegalMode);
            if (_moveOptions is not null && key.Equals(_moveOptionsKey))
                return _moveOptions;
            _moveOptionsKey = key;
            var learn = CoreAdapter.GetLearnableMoves(_pk);
            var names = CoreAdapter.MoveNames;
            var list = new List<MoveOption>();
            for (int i = 1; i <= _pk.MaxMoveID && i < names.Count; i++)
            {
                // Modo legal: so os golpes que o Pokemon aprende de forma legal.
                if (!string.IsNullOrWhiteSpace(names[i]) && (learn[i] || !LegalMode))
                    list.Add(new MoveOption(i, names[i], learn[i]));
            }
            _moveOptions = [.. list.OrderByDescending(o => o.IsLearnable).ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)];
            return _moveOptions;
        }
    }
    private IReadOnlyList<MoveOption>? _moveOptions;
    private object? _moveOptionsKey;

    /// <summary>Especie escolhida pelo nome (AutoCompleteBox). Texto parcial e ignorado ate virar um nome valido.</summary>
    public object? SelectedSpeciesName
    {
        get => SpeciesName;
        set
        {
            var index = CoreAdapter.FindIndex(CoreAdapter.SpeciesNames, value as string);
            if (index > 0 && (!LegalMode || SpeciesNames.Contains(CoreAdapter.SpeciesNames[index])))
                Species = index;
        }
    }
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
    /// <param name="restore">Modo legal: versao para voltar se nao der para legalizar (troca de especie, Showdown).</param>
    private async Task LegalizeAsync(PKM? restore = null)
    {
        if (_sav is null)
        {
            RestoreAfterFailedLegalize(restore);
            _guardSuspended = false;
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
                RestoreAfterFailedLegalize(restore);
                _status(restore is null ? $"Legalizar: {message}." : $"Modo legal: mudança desfeita, não há forma legal ({message}).");
                return;
            }
            _pk = result;
            _isNew = false;
            RaiseAll();
            _status($"Legalizado a partir de: {message}. Confira e clique em Aplicar para gravar.");
        }
        catch (Exception ex)
        {
            RestoreAfterFailedLegalize(restore);
            _status($"Legalizar: erro ({ex.Message})");
        }
        finally
        {
            IsLegalizing = false;
            _guardSuspended = false;
        }
    }
    private void RestoreAfterFailedLegalize(PKM? restore)
    {
        if (restore is null)
            return;
        _pk = restore;
        _isNew = CoreAdapter.IsEmpty(restore);
        RaiseAll();
    }

    /// <summary>
    /// Modo legal: uma mudanca grande (outra especie, set Showdown) quase sempre invalida o encontro. Em vez de
    /// desfazer, gera de novo a partir de um encontro real; se nao houver, volta para <paramref name="before"/>.
    /// </summary>
    private void LegalizeOrRestore(PKM before)
    {
        if (!LegalMode || IsLegal)
        {
            _guardSuspended = false;
            return;
        }
        _ = LegalizeAsync(before);
    }

    // Evoluir por troca
    private (ushort, byte, int) _tradeKey = (ushort.MaxValue, 0, 0);
    private IReadOnlyList<TradeEvolutionOption> _tradeEvolutions = [];
    /// <summary>Evolucoes por troca da especie atual (ex.: Kadabra → Alakazam, Onix + Metal Coat → Steelix).</summary>
    public IReadOnlyList<TradeEvolutionOption> TradeEvolutions
    {
        get
        {
            var key = (_pk.Species, _pk.Form, _pk.HeldItem);
            if (key != _tradeKey)
            {
                _tradeKey = key;
                _tradeEvolutions = [.. CoreAdapter.GetTradeEvolutions(_pk).Select(e => new TradeEvolutionOption(e, new RelayCommand(() => EvolveByTrade(e))))];
            }
            return _tradeEvolutions;
        }
    }
    public bool HasTradeEvolutions => TradeEvolutions.Count > 0;

    private void EvolveByTrade(CoreAdapter.TradeEvolution evo)
    {
        if (evo.Blocked is { } why)
        {
            _status($"Não evolui: {SpeciesName} {why}. Tire o item e tente de novo.");
            return;
        }
        try
        {
            var done = CoreAdapter.EvolveByTrade(_pk, evo, _sav);
            _isNew = false;
            RaiseAll();
            _status(LegalityStatus(done));
        }
        catch (Exception ex)
        {
            _status($"Evoluir por troca: erro ({ex.Message})");
        }
    }

    public RelayCommand SuggestRelearnCommand { get; }
    public RelayCommand SuggestMetCommand { get; }
    public RelayCommand FixIVsCommand { get; }
    /// <summary>Golpes de reaprender so existem a partir da Gen 6.</summary>
    public bool HasRelearnMoves => _pk.Format >= 6;
    public IReadOnlyList<string> LegalityIssues { get; private set; } = [];
    public bool HasLegalityIssues => ShowLegality && LegalityIssues.Count > 0;
    /// <summary>Legal, mas com avisos (ex.: sequencia RNG suspeita).</summary>
    public bool HasWarnings => ShowLegal && LegalityIssues.Count > 0;
    /// <summary>Legalizar aparece para ilegal e tambem para legal com avisos.</summary>
    public bool ShowLegalize => ShowIllegal || HasWarnings;

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
    public int Species
    {
        get => _pk.Species;
        set
        {
            if (value < 0 || value == _pk.Species)
                return;
            var before = _pk.Clone();
            _guardSuspended = LegalMode;
            CoreAdapter.ChangeSpecies(_pk, (ushort)value);
            _isNew = false;
            RaiseAll();
            LegalizeOrRestore(before);
        }
    }
    public int Level { get => _pk.CurrentLevel; set { _pk.CurrentLevel = (byte)Math.Clamp(value, 1, 100); Refresh(); } }
    public int Nature { get => (int)_pk.StatAlignment; set { if (value >= 0 && value != (int)_pk.StatAlignment) { _pk.SetNature((Nature)value); Refresh(); } } }
    public int HeldItem { get => _pk.HeldItem; set { if (value >= 0) { _pk.HeldItem = value; Refresh(); Raise(nameof(SelectedItem)); Raise(nameof(HasItem)); Raise(nameof(HeldItemIcon)); Raise(nameof(HeldItemTip)); } } }
    /// <summary>Descricao e onde conseguir o item segurado (AllGenWiki), quando houver.</summary>
    public string? HeldItemTip => _pk.HeldItem > 0 ? ItemInfo.GetTooltip(CoreAdapter.GetHeldItemName(_pk), _pk.Context.Generation) : null;
    /// <summary>Icone do item segurado (ao lado do campo Item).</summary>
    public Bitmap? HeldItemIcon => SpriteService.GetItemSprite(_pk.HeldItem, _pk.Context);

    /// <summary>Itens que podem ser segurados neste jogo, em ordem alfabetica (numeracao certa por geracao).</summary>
    public IReadOnlyList<ComboItem> ItemOptions => _itemOptions ??= [.. CoreAdapter.GetHeldItemOptions()
        .Where(i => i.Value > 0 && !string.IsNullOrWhiteSpace(i.Text))
        .OrderBy(i => i.Text, StringComparer.CurrentCultureIgnoreCase)];
    private IReadOnlyList<ComboItem>? _itemOptions;

    /// <summary>Item escolhido na lista com sugestoes. Texto parcial e ignorado ate virar um item.</summary>
    public object? SelectedItem
    {
        get
        {
            var item = _pk.HeldItem;
            if (item == 0)
                return null;
            return ItemOptions.FirstOrDefault(i => i.Value == item)
                ?? new ComboItem(CoreAdapter.GetHeldItemName(_pk) is { Length: > 0 } n ? n : $"Item #{item}", item);
        }
        set
        {
            var id = value switch
            {
                ComboItem c => c.Value,
                string s => ItemOptions.FirstOrDefault(i => string.Equals(i.Text, s.Trim(), StringComparison.OrdinalIgnoreCase))?.Value ?? -1,
                _ => -1,
            };
            if (id > 0)
                HeldItem = id;
        }
    }
    public bool HasItem => _pk.HeldItem > 0;
    public RelayCommand ClearItemCommand => new(() => HeldItem = 0);
    /// <summary>Os 4 golpes (tipo, PP e PP Ups). Use Moves[i].Move para trocar um golpe.</summary>
    public IReadOnlyList<MoveSlotViewModel> Moves { get; }
    // Atalhos mantidos para codigo existente (testes, Showdown).
    public int Move1 { get => Moves[0].Move; set => Moves[0].Move = value; }
    public int Move2 { get => Moves[1].Move; set => Moves[1].Move = value; }
    public int Move3 { get => Moves[2].Move; set => Moves[2].Move = value; }
    public int Move4 { get => Moves[3].Move; set => Moves[3].Move = value; }
    public RelayCommand HealPPCommand { get; }
    /// <summary>PP Ups so existem a partir da Gen 1 em formato com PP; em Let's Go/Legends nao ha.</summary>
    public bool HasPPUps => _pk is not (PB7 or PA8 or PA9);

    // Encontro
    private readonly IReadOnlyList<ComboItem> _allBalls;
    /// <summary>Bolas do jogo; no modo legal, so as permitidas para o encontro (mais a atual).</summary>
    public IReadOnlyList<ComboItem> BallList { get; private set; } = [];
    private object? _ballKey;
    private bool _ballReselect;
    public IReadOnlyList<ComboItem> MetLocationList { get; }
    public ComboItem? Ball
    {
        get => _ballReselect ? null : Find(BallList, _pk.Ball);
        set { if (value is not null && !_ballReselect) { _pk.Ball = (byte)value.Value; Refresh(); } }
    }

    /// <summary>Recalcula a lista de bolas quando especie/encontro mudam (so no modo legal ela depende do Pokemon).</summary>
    private void RefreshBalls()
    {
        object key = LegalMode ? (_pk.Species, _pk.Form, _pk.Version, _pk.MetLocation, _pk.MetLevel, _pk.IsEgg, _pk.Ball, IsLegal) : "all";
        if (key.Equals(_ballKey))
            return;
        _ballKey = key;
        if (!LegalMode)
            BallList = _allBalls;
        else
        {
            var legal = CoreAdapter.GetLegalBalls(_pk);
            BallList = [.. _allBalls.Where(b => legal.Contains(b.Value) || b.Value == _pk.Ball)];
        }
        Raise(nameof(BallList));
        // A ComboBox perde a selecao quando a lista muda: limpa e seleciona de novo depois do layout.
        _ballReselect = true;
        Raise(nameof(Ball));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { _ballReselect = false; Raise(nameof(Ball)); }, Avalonia.Threading.DispatcherPriority.Background);
    }
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
        foreach (var m in Moves)
            m.RaiseAll();
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
        var before = _pk.Clone();
        _guardSuspended = LegalMode;
        var error = CoreAdapter.ApplyShowdown(_pk, text);
        if (error is null)
            _isNew = false;
        RaiseAll(); // atualiza todos os campos
        _status(error ?? "Set Showdown importado. Clique em Aplicar para gravar.");
        LegalizeOrRestore(before);
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
        foreach (var p in (string[])[nameof(RadarValues), nameof(StatTotal), nameof(EVTotal), nameof(IVTotal), nameof(EVSummary), nameof(IVSummary), nameof(Types), nameof(GenderSymbol), nameof(SpeciesName), nameof(SelectedSpeciesName), nameof(MoveOptions)])
            Raise(p);
        (IsLegal, LegalityReport) = CoreAdapter.CheckLegality(_pk);
        LegalityText = IsLegal ? "Legal" : "Ilegal";
        LegalityIssues = CoreAdapter.GetLegalityIssues(_pk); // ilegal: problemas; legal: avisos "Fishy"
        foreach (var p in (string[])[nameof(Sprite), nameof(IsLegal), nameof(ShowLegality), nameof(ShowLegal), nameof(ShowIllegal), nameof(LegalityIssues), nameof(HasLegalityIssues), nameof(HasWarnings), nameof(ShowLegalize), nameof(LegalityText), nameof(LegalityReport), nameof(AbilityName), nameof(IsShiny), nameof(PID), nameof(EncryptionConstant)])
            Raise(p);
        RaiseLegalMode();
        GuardLegality();
        RefreshBalls();
    }
}

public sealed record TypeChip(string Name, uint Argb)
{
    public Avalonia.Media.IBrush Brush { get; } = new Avalonia.Media.SolidColorBrush(Argb);
}

/// <summary>Um golpe no editor: escolha do golpe, chip do tipo, barra de PP e PP Ups.</summary>
/// <summary>Uma opcao da lista de golpes. <see cref="IsLearnable"/> = aprende oficialmente (fundo verde, no topo).</summary>
public sealed record MoveOption(int Index, string Name, bool IsLearnable)
{
    public override string ToString() => Name;
}

public sealed class MoveSlotViewModel(Func<PKM> pk, int index, Func<IReadOnlyList<MoveOption>> options) : ViewModelBase
{
    public event Action? Changed;
    public void RaiseAll() => Raise(string.Empty);

    public int Index { get; } = index;

    /// <summary>Indice na lista de golpes. Trocar o golpe enche o PP.</summary>
    public int Move
    {
        get => CoreAdapter.GetMove(pk(), Index);
        set
        {
            if (value < 0 || value == Move)
                return;
            var p = pk();
            CoreAdapter.SetMove(p, Index, (ushort)value);
            if (value == 0)
                CoreAdapter.SetPPUps(p, Index, 0);
            CoreAdapter.SetPP(p, Index, CoreAdapter.GetMaxPP(p, Index));
            RaiseAll();
            Changed?.Invoke();
        }
    }

    public bool HasMove => Move > 0;

    /// <summary>Golpe escolhido na lista com sugestoes. Texto parcial e ignorado ate virar um golpe.</summary>
    public object? MoveName
    {
        get
        {
            var move = Move;
            if (move == 0)
                return null;
            // Golpe atual fora da lista (ex.: ilegal no modo legal): continua aparecendo no campo.
            return options().FirstOrDefault(o => o.Index == move)
                ?? new MoveOption(move, (uint)move < (uint)CoreAdapter.MoveNames.Count ? CoreAdapter.MoveNames[move] : $"#{move}", false);
        }
        set
        {
            // Texto digitado so vale se estiver na lista (no modo legal, so golpes que aprende).
            var index = value switch
            {
                MoveOption o => o.Index,
                string s => options().FirstOrDefault(o => string.Equals(o.Name, s.Trim(), StringComparison.OrdinalIgnoreCase))?.Index ?? -1,
                _ => -1,
            };
            if (index > 0)
                Move = index;
        }
    }

    /// <summary>O golpe atual esta entre os que o Pokemon aprende (borda verde no campo).</summary>
    public bool IsLearnable => MoveName is MoveOption { IsLearnable: true };
    public RelayCommand ClearCommand => new(() => Move = 0);
    private (string Name, uint Argb)? Type => CoreAdapter.GetMoveType((ushort)Move, pk().Context);
    public string TypeName => Type?.Name ?? "";
    public Avalonia.Media.IBrush TypeBrush => new Avalonia.Media.SolidColorBrush(Type?.Argb ?? 0x00000000);

    public int MaxPP => CoreAdapter.GetMaxPP(pk(), Index);
    public int PP
    {
        get => CoreAdapter.GetPP(pk(), Index);
        set
        {
            CoreAdapter.SetPP(pk(), Index, Math.Clamp(value, 0, MaxPP));
            Raise(); Raise(nameof(PPText)); Raise(nameof(PPRatio));
            Changed?.Invoke();
        }
    }
    public string PPText => HasMove ? $"PP {PP}/{MaxPP}" : "";
    public double PPRatio => MaxPP == 0 ? 0 : (double)PP / MaxPP;

    public IReadOnlyList<string> PPUpOptions { get; } = ["+0", "+1", "+2", "+3"];
    /// <summary>PP Ups (0-3). Ao mudar, o PP vai para o novo maximo.</summary>
    public int PPUps
    {
        get => CoreAdapter.GetPPUps(pk(), Index);
        set
        {
            if (value is < 0 or > 3 || !HasMove)
                return;
            var p = pk();
            CoreAdapter.SetPPUps(p, Index, value);
            CoreAdapter.SetPP(p, Index, CoreAdapter.GetMaxPP(p, Index));
            RaiseAll();
            Changed?.Invoke();
        }
    }
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

/// <summary>Botao "Evoluir por troca" de um destino.</summary>
public sealed class TradeEvolutionOption(CoreAdapter.TradeEvolution evo, RelayCommand command)
{
    public string Label => $"Evoluir para {evo.Name}";
    public string Requirement => evo.Requirement;
    public bool IsBlocked => evo.Blocked is not null;
    public string Tooltip => evo.Blocked is { } why
        ? $"Não dá: {why}."
        : $"Simula a troca ({evo.Requirement}){(evo.ItemId > 0 ? "; o item é consumido se estiver segurando" : "")}. Clique em Aplicar para gravar.";
    public RelayCommand Command { get; } = command;
}
