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
public sealed partial class PokemonEditorViewModel : ViewModelBase
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
        Moves = [.. Enumerable.Range(0, 4).Select(i => new MoveSlotViewModel(() => _pk, i, () => MoveOptions, () => TipVersion))];
        foreach (var m in Moves)
            m.Changed += Refresh;
        HealPPCommand = new RelayCommand(() => { _pk.HealPP(); foreach (var m in Moves) m.RaiseAll(); _status("PP restaurado."); });
        ApplyCommand = new RelayCommand(Apply, () => CanApply);
        GivePokerusCommand = new RelayCommand(() => SetPokerus(1, Pokerus.GetMaxDuration(1)), () => CanEditPokerus);
        MaxIVsCommand = new RelayCommand(() => _ = MaxIVsAsync());
        ClearEVsCommand = new RelayCommand(() => { foreach (var s in Stats) s.EV = 0; });
        MakeShinyCommand = new RelayCommand(MakeShiny, () => CanMakeShiny);
        Markings = [.. Enumerable.Range(0, CoreAdapter.GetMarkingCount(_pk)).Select(i => new MarkingViewModel(() => _pk, i, Refresh))];
        HyperTraining = _pk is IHyperTrain
            ? [.. Enumerable.Range(0, 6).Select(i => new HyperTrainViewModel(() => _pk, i, StatLabelsShort[i], Refresh))]
            : [];
        ContestStats = _pk is IContestStats
            ? [.. Enumerable.Range(0, 6).Select(i => new ContestStatViewModel(() => _pk, i, () => OnContestChanged(i)))]
            : [];
        OTMemory = _pk is IMemoryOT ? new MemoryViewModel(() => _pk, true, Refresh) : null;
        HTMemory = _pk is IMemoryHT ? new MemoryViewModel(() => _pk, false, Refresh) : null;
        AddAllRibbonsCommand = new RelayCommand(() => { CoreAdapter.SetAllValidRibbons(_pk); RaiseAll(); _status(LegalityStatus("Fitas legais adicionadas")); });
        RemoveAllRibbonsCommand = new RelayCommand(() => { CoreAdapter.RemoveAllRibbons(_pk); RaiseAll(); _status(LegalityStatus("Fitas removidas")); });
        LegalizeCommand = new RelayCommand(() => _ = LegalizeAsync(), () => !IsLegalizing);
        SuggestMovesCommand = new RelayCommand(() => Fix("Golpes sugeridos", pk => CoreAdapter.SuggestMoves(pk)));
        SuggestRelearnCommand = new RelayCommand(() => Fix("Golpes de reaprender", pk => CoreAdapter.SuggestRelearnMoves(pk)));
        SuggestMetCommand = new RelayCommand(() => Fix("Encontro sugerido", pk => _sav is null ? CoreAdapter.SuggestMetData(pk) : EncounterDatabase.SuggestMet(_sav, pk), "nenhum encontro possível para esta espécie neste jogo"));
        _allBalls = CoreAdapter.GetBalls();
        MetLocationList = CoreAdapter.GetMetLocations(_pk);
        Refresh();
        _viewReady = true;
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
            RaiseHiddenPower();
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
        foreach (var p in (string[])[nameof(CanApply), nameof(ShowLegalModeBlock), nameof(ApplyTip), nameof(HasContestStats), nameof(ContestNote), nameof(CanEditPokerus), nameof(PokerusNote)])
            Raise(p);
        ApplyCommand.NotifyCanExecuteChanged();
        GivePokerusCommand.NotifyCanExecuteChanged();
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
                    list.Add(new MoveOption(i, names[i], learn[i], _pk.Format, TipVersion));
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
    /// <param name="only">Gerar a partir deste encontro (lista "Trocar encontro").</param>
    private async Task LegalizeAsync(PKM? restore = null, IEncounterInfo? only = null)
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
            var (result, message) = await Task.Run(() => (EncounterDatabase.Legalize(sav, current, out var m, default, only), m));
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

    /// <summary>Perguntas ao usuario (titulo, mensagem, botao de confirmar), definida pelo MainViewModel.</summary>
    public Func<string, string, string, Task<bool>>? Confirm { get; init; }

    /// <summary>
    /// IVs maximos. Com o modo legal, se IVs 31 deixariam o Pokemon ilegal (Gen 3/4: IVs ligados ao PID nos encontros
    /// selvagens) e a especie nasce de ovo neste jogo, pergunta se pode converter em nascido de ovo, onde os IVs sao livres.
    /// </summary>
    private async Task MaxIVsAsync()
    {
        var test = _pk.Clone();
        Span<int> max = stackalloc int[6];
        max.Fill(_pk.MaxIV);
        test.SetIVs(max);
        if (!LegalMode || !IsLegal || _sav is null || new LegalityAnalysis(test).Valid)
        {
            foreach (var s in Stats)
                s.IV = _pk.MaxIV;
            _status(LegalityStatus("IVs máximos: aplicado"));
            return;
        }
        var name = SpeciesName;
        var sav = _sav;
        if (!EncounterDatabase.CanHatch(sav, _pk.Species))
        {
            _status($"Modo legal: {name} não pode ter todos os IVs no máximo (os IVs deste encontro são ligados ao PID) e não nasce de ovo neste jogo.");
            return;
        }
        if (Confirm is null || !await Confirm("Converter em nascido de ovo?",
                $"{name} veio de um encontro em que o jogo gera os IVs junto com o PID, então IVs 31 em tudo seria ilegal. "
                + $"Pokémon nascidos de ovo podem ter qualquer IV. Converter {name} em nascido de ovo (deste jogo) com todos os IVs no máximo? "
                + "Natureza, gênero, shiny, nível, item, apelido e golpes são mantidos quando possível; o local e a data de encontro mudam.",
                "Converter em ovo"))
            return;
        IsLegalizing = true;
        _status($"Convertendo {name} em nascido de ovo...");
        try
        {
            var current = _pk.Clone();
            var (result, message) = await Task.Run(() => (EncounterDatabase.ConvertToEggHatched(sav, current, out var m), m));
            if (result is null)
            {
                _status($"Não deu para converter: {message}.");
                return;
            }
            _pk = result;
            _isNew = false;
            RaiseAll();
            _status($"{name} agora é nascido de ovo, com IVs máximos ({message}). Confira e clique em Aplicar para gravar.");
        }
        catch (Exception ex)
        {
            _status($"Converter em ovo: erro ({ex.Message})");
        }
        finally
        {
            IsLegalizing = false;
        }
    }

    // Evoluir por item (pedras e afins)
    private (ushort, byte, byte) _itemEvoKey = (ushort.MaxValue, 0, 0);
    private IReadOnlyList<ItemEvolutionOption> _itemEvolutions = [];
    /// <summary>Evolucoes por item da especie atual (ex.: Pikachu + Thunder Stone → Raichu).</summary>
    public IReadOnlyList<ItemEvolutionOption> ItemEvolutions
    {
        get
        {
            var key = (_pk.Species, _pk.Form, _pk.Gender);
            if (key != _itemEvoKey)
            {
                _itemEvoKey = key;
                _itemEvolutions = [.. CoreAdapter.GetItemEvolutions(_pk).Select(e => new ItemEvolutionOption(e, new RelayCommand(() => EvolveByItem(e))))];
            }
            return _itemEvolutions;
        }
    }
    public bool HasItemEvolutions => ItemEvolutions.Count > 0;

    private void EvolveByItem(CoreAdapter.ItemEvolution evo)
    {
        if (evo.Blocked is { } why)
        {
            _status($"Não evolui: {why}.");
            return;
        }
        try
        {
            var done = CoreAdapter.EvolveByItem(_pk, evo);
            _isNew = false;
            RaiseAll();
            _status(LegalityStatus(done));
        }
        catch (Exception ex)
        {
            _status($"Evoluir por item: erro ({ex.Message})");
        }
    }

    public IReadOnlyList<FriendshipEvolutionOption> FriendshipEvolutions => [.. CoreAdapter.GetFriendshipEvolutions(_pk, _sav)
        .Select(e => new FriendshipEvolutionOption(e, new RelayCommand(() => EvolveByFriendship(e))))];
    public bool HasFriendshipEvolutions => FriendshipEvolutions.Count > 0;
    private void EvolveByFriendship(CoreAdapter.FriendshipEvolution evo)
    {
        try
        {
            var candidate = _pk.Clone();
            var message = CoreAdapter.EvolveByFriendship(candidate, evo, _sav);
            if (LegalMode && !new LegalityAnalysis(candidate).Valid)
            {
                _status("Não foi possível evoluir mantendo a legalidade: " + string.Join(" / ", CoreAdapter.GetLegalityIssues(candidate, 2)));
                return;
            }
            _pk = candidate;
            _isNew = false;
            RaiseAll();
            _status(LegalityStatus(message));
        }
        catch (Exception ex) { _status("Não evolui: " + ex.Message); }
    }

    public IReadOnlyList<ItemEvolutionOption> BeautyEvolutions => [.. CoreAdapter.GetBeautyEvolutions(_pk, _sav)
        .Select(e => new ItemEvolutionOption(e, new RelayCommand(() => EvolveByBeauty(e)), isBeauty: true))];
    public bool HasBeautyEvolutions => BeautyEvolutions.Count > 0;
    private void EvolveByBeauty(CoreAdapter.ItemEvolution evo)
    {
        try
        {
            var candidate = _pk.Clone();
            var message = CoreAdapter.EvolveByBeauty(candidate, evo, _sav);
            if (LegalMode && !new LegalityAnalysis(candidate).Valid)
            {
                _status("Não foi possível evoluir mantendo a legalidade: " + string.Join(" / ", CoreAdapter.GetLegalityIssues(candidate, 2)));
                return;
            }
            _pk = candidate;
            _isNew = false;
            RaiseAll();
            _status(LegalityStatus(message));
        }
        catch (Exception ex) { _status("Não evolui: " + ex.Message); }
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
            _status($"Não evolui: {SpeciesName} {why}.");
            return;
        }
        try
        {
            var candidate = _pk.Clone();
            var done = CoreAdapter.EvolveByTrade(candidate, evo, _sav);
            if (LegalMode && !new LegalityAnalysis(candidate).Valid)
            {
                _status("Não foi possível evoluir mantendo a legalidade: " + string.Join(" / ", CoreAdapter.GetLegalityIssues(candidate, 2)));
                return;
            }
            _pk = candidate;
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
        // Testa numa copia: no modo legal, uma correcao que deixaria o Pokemon ilegal nao e aplicada (e o motivo aparece).
        var test = _pk.Clone();
        bool? changed;
        try { changed = apply(test); }
        catch (Exception ex) { _status($"{what}: erro ({ex.Message})"); return; }
        if (changed is null)
        {
            _status($"{what}: {whenNull ?? "indisponível"}.");
            return;
        }
        if (changed == false)
        {
            _status($"{what}: nada a mudar{(IsLegal ? "" : " (o problema está em outro ponto; tente ✨ Legalizar)")}.");
            return;
        }
        if (LegalMode && IsLegal && !new LegalityAnalysis(test).Valid)
        {
            var why = CoreAdapter.GetLegalityIssues(test, 1);
            _status($"Modo legal: {what.ToLowerInvariant()} não aplicado, deixaria o Pokémon ilegal ({(why.Count > 0 ? why[0] : "?")}).");
            return;
        }
        _pk = test;
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
    public string? HeldItemTip => _pk.HeldItem > 0 ? ItemInfo.GetTooltip(CoreAdapter.GetHeldItemName(_pk), _pk.Context.Generation, TipVersion) : null;
    /// <summary>Jogo usado nas dicas "onde conseguir/aprender" (o do save aberto).</summary>
    private GameVersion TipVersion => _sav?.Version ?? _pk.Version;
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

    /// <summary>
    /// Recalcula as listas que dependem do Pokemon (bola, habilidade, forma, Tera Type) quando especie/encontro mudam.
    /// No modo legal, cada opcao e testada numa copia e so ficam as legais (mais a atual).
    /// </summary>
    private void RefreshBalls()
    {
        object key = (LegalMode, IsLegal, (_pk.Species, _pk.Form, _pk.Version, _pk.MetLocation, _pk.MetLevel, _pk.IsEgg),
            (_pk.Ball, _pk.PID, _pk.AbilityNumber, CoreAdapter.GetTeraType(_pk)));
        if (key.Equals(_ballKey))
            return;
        _ballKey = key;
        bool filter = LegalMode && IsLegal;
        IReadOnlyList<ComboItem> balls;
        if (!LegalMode)
            balls = _allBalls;
        else
        {
            var legal = CoreAdapter.GetLegalBalls(_pk);
            balls = [.. _allBalls.Where(b => legal.Contains(b.Value) || b.Value == _pk.Ball)];
        }
        var abilityIndex = CoreAdapter.GetAbilityIndex(_pk);
        IReadOnlyList<ComboItem> abilities = [.. CoreAdapter.GetAbilityOptions(_pk)
            .Where(a => !filter || a.Value == abilityIndex || CoreAdapter.IsLegalWith(_pk, p => CoreAdapter.SetAbilityIndex(p, a.Value)))];
        IReadOnlyList<FormOption> forms = _sav is null ? [] : [.. CoreAdapter.GetFormOptions(_pk, _sav.Personal, hideBattleOnly: LegalMode)
            .Select(f => new FormOption(f.Text, f.Value, _pk.Species, _pk.IsShiny, _pk.Gender, _pk.Context))];
        var tera = CoreAdapter.GetTeraType(_pk);
        IReadOnlyList<ComboItem> teras = tera < 0 ? [] : [.. CoreAdapter.GetTeraOptions()
            .Where(t => !filter || t.Value == tera || CoreAdapter.IsLegalWith(_pk, p => CoreAdapter.SetTeraType(p, t.Value)))];
        IsShinyLocked = LegalMode && !_pk.IsShiny && CoreAdapter.IsShinyLocked(_pk);
        CurrentEncounter = CoreAdapter.GetCurrentEncounterLabel(_pk);
        foreach (var p in (string[])[nameof(IsShinyLocked), nameof(CanMakeShiny), nameof(ShinyTip), nameof(CurrentEncounter), nameof(AbilityTip), nameof(ShinySymbol), nameof(CanToggleShiny), nameof(ShinyShortcutsTip), nameof(ShinyActionText), nameof(CanToggleGender), nameof(GenderTip), nameof(HasFormNote)])
            Raise(p);
        MakeShinyCommand.NotifyCanExecuteChanged();

        // So troca a lista se o conteudo mudou: a ComboBox perde a selecao (e devolve a antiga) quando recebe outra lista.
        var changed = new List<string>();
        if (!SameItems(BallList, balls)) { BallList = balls; changed.Add(nameof(BallList)); }
        if (!SameItems(AbilityOptions, abilities)) { AbilityOptions = abilities; changed.Add(nameof(AbilityOptions)); }
        if (!SameItems(FormOptions, forms)) { FormOptions = forms; changed.AddRange([nameof(FormOptions), nameof(HasForms)]); }
        if (!SameItems(TeraOptions, teras)) { TeraOptions = teras; changed.AddRange([nameof(TeraOptions), nameof(HasTera)]); }
        if (changed.Count == 0 || !_viewReady)
        {
            // Ainda no construtor (nenhuma tela ligada): lista e selecao vao juntas, sem o "limpa e seleciona de novo".
            foreach (var p in changed)
                Raise(p);
            RaiseSelections();
            return;
        }
        // Ignora o que a ComboBox devolver enquanto troca de lista; depois do layout, seleciona de novo.
        _ballReselect = true;
        foreach (var p in changed)
            Raise(p);
        RaiseSelections();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { _ballReselect = false; RaiseSelections(); }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private static bool SameItems(IReadOnlyList<FormOption> a, IReadOnlyList<FormOption> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    private static bool SameItems(IReadOnlyList<ComboItem> a, IReadOnlyList<ComboItem> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Value != b[i].Value || a[i].Text != b[i].Text)
                return false;
        }
        return true;
    }

    /// <summary>Falso so durante o construtor: a tela ainda nao esta ligada as listas.</summary>
    private bool _viewReady;

    private void RaiseSelections()
    {
        foreach (var p in (string[])[nameof(Ball), nameof(SelectedAbility), nameof(SelectedForm), nameof(SelectedTera)])
            Raise(p);
    }

    // Habilidade
    public IReadOnlyList<ComboItem> AbilityOptions { get; private set; } = [];
    public ComboItem? SelectedAbility
    {
        get => _ballReselect ? null : Find(AbilityOptions, CoreAdapter.GetAbilityIndex(_pk));
        set
        {
            if (value is null || _ballReselect || value.Value == CoreAdapter.GetAbilityIndex(_pk))
                return;
            CoreAdapter.SetAbilityIndex(_pk, value.Value);
            RaiseAll();
        }
    }
    /// <summary>Descricao da habilidade (AllGenWiki) e, na Gen 3-5, o aviso de que ela vem do PID.</summary>
    public string? AbilityTip
    {
        get
        {
            var text = GameText.GetAbility(AbilityName, _pk.Format);
            if (LegalMode && _pk.Format <= 5 && AbilityOptions.Count > 1)
                text = (text is null ? "" : text + "\n\n") + "Na Gen 3–5 a habilidade depende do PID: para trocar mantendo legal, use ✨ Legalizar.";
            return text;
        }
    }

    // Forma (cada opcao com o sprite da forma)
    public IReadOnlyList<FormOption> FormOptions { get; private set; } = [];
    public bool HasForms => FormOptions.Count > 1 && !HasFormNote;
    /// <summary>Deoxys na Gen 3: a forma nao fica no Pokemon, depende do jogo em que ele esta.</summary>
    public bool HasFormNote => _pk.Format == 3 && _pk.Species == (ushort)PKHeX.Core.Species.Deoxys;
    public string FormNote => "Na Gen 3, a forma do Deoxys depende do jogo em que ele está: Normal em Ruby/Sapphire, Ataque no FireRed, Defesa no LeafGreen e Velocidade no Emerald.";

    // Genero (clicavel)
    /// <summary>Da para trocar o genero: especies com os dois generos (ou cuja forma e o genero), da Gen 3 em diante.</summary>
    public bool CanToggleGender => !CoreAdapter.IsEmpty(_pk) && !_pk.IsEgg && _pk.Format >= 3
        && (_pk.PersonalInfo.IsDualGender || (CoreAdapter.IsGenderForm(_pk.Species) && _pk.Format >= 6));
    public string GenderTip => !CanToggleGender ? "Gênero" :
        "Clique para trocar o gênero." + (CoreAdapter.IsGenderForm(_pk.Species) ? " A forma muda junto (nesta espécie, a forma é o gênero)." : "")
        + (_pk.Gen3 || _pk.Gen4 || _pk.Gen5 ? " Até a Gen 5 o gênero vem do PID: um PID novo é gerado (mesma natureza)." : "");

    public void ToggleGender()
    {
        if (!CanToggleGender)
            return;
        var gender = (byte)(_pk.Gender == 0 ? 1 : 0);
        var before = _pk.Clone();
        _guardSuspended = LegalMode;
        CoreAdapter.SetGender(_pk, gender);
        _isNew = false;
        RaiseAll();
        if (LegalMode)
            LegalizeOrRestore(before); // PID novo ou outra forma: se ficar ilegal, gera de novo (ou volta)
        else
            _guardSuspended = false;
        _status($"Gênero: {CoreAdapter.GetGenderSymbol(_pk)}" + (CoreAdapter.IsGenderForm(_pk.Species) ? " (forma trocada junto)" : ""));
    }

    public FormOption? SelectedForm
    {
        get => _ballReselect ? null : FormOptions.FirstOrDefault(f => f.Value == _pk.Form);
        set
        {
            if (value is null || _ballReselect || value.Value == _pk.Form)
                return;
            // Outra forma costuma ser outro encontro: no modo legal, gera de novo (ou volta se nao der).
            var before = _pk.Clone();
            _guardSuspended = LegalMode;
            CoreAdapter.SetForm(_pk, (byte)value.Value);
            _isNew = false;
            RaiseAll();
            LegalizeOrRestore(before);
        }
    }

    // Tera Type (Scarlet/Violet)
    public IReadOnlyList<ComboItem> TeraOptions { get; private set; } = [];
    public bool HasTera => TeraOptions.Count > 0;
    public ComboItem? SelectedTera
    {
        get => _ballReselect ? null : Find(TeraOptions, CoreAdapter.GetTeraType(_pk));
        set
        {
            if (value is null || _ballReselect || value.Value == CoreAdapter.GetTeraType(_pk))
                return;
            CoreAdapter.SetTeraType(_pk, value.Value);
            Refresh();
        }
    }

    // Shiny
    /// <summary>Modo legal: o encontro nunca e shiny (shiny lock).</summary>
    public bool IsShinyLocked { get; private set; }
    public bool CanMakeShiny => !_pk.IsShiny && !IsShinyLocked && !CoreAdapter.IsEmpty(_pk);
    public string ShinyTip => _pk.IsShiny ? "Já é shiny."
        : IsShinyLocked ? "Modo legal: este encontro nunca é shiny (shiny lock)."
        : LegalMode ? "Gera de novo a partir do encontro já como shiny, mantendo natureza, nível, item e golpes."
        : "Gera um PID shiny (na Gen 3/4 a natureza pode mudar).";

    /// <summary>Simbolo do shiny no cabecalho: ☆ normal, ★ estrela, ◆ quadrado (o quadrado so existe a partir da Gen 8).</summary>
    public string ShinySymbol => !_pk.IsShiny ? "☆"
        : _pk.Context.IsSquareShinyDifferentiated && ShinyExtensions.GetType(_pk) == Shiny.AlwaysSquare ? "◆" : "★";
    public bool CanToggleShiny => !CoreAdapter.IsEmpty(_pk) && !_pk.IsEgg;
    public string ShinyActionText => _pk.IsShiny ? "☆  Tirar shiny" : "★  Tornar shiny";
    public string ShinyShortcutsTip => (_pk.IsShiny ? "Clique: tira o shiny." : "Clique: torna shiny (troca o PID).")
        + "\nAlt+clique: shiny mantendo o PID (troca o SID do treinador; mantém a ligação PID/IV da Gen 3/4)."
        + "\nShift+clique: shiny quadrado · Ctrl+clique: shiny estrela (a diferença só aparece a partir da Gen 8)."
        + (IsShinyLocked && LegalMode ? "\nEste encontro nunca é shiny (shiny lock): no modo legal, não dá." : "");

    /// <summary>
    /// Clique na estrela (ou em "Tornar shiny") com os atalhos do PKHeX: Alt mantem o PID e troca o SID,
    /// Shift pede shiny quadrado, Ctrl shiny estrela; sem modificador, alterna (torna shiny ou tira).
    /// </summary>
    public void ShinyClick(bool alt, bool shift, bool ctrl)
    {
        if (!CanToggleShiny)
            return;
        var type = shift ? Shiny.AlwaysSquare : ctrl ? Shiny.AlwaysStar : Shiny.Random;
        bool wantsShape = shift || ctrl;

        // Ja shiny e sem pedir formato: tira o shiny.
        if (_pk.IsShiny && !wantsShape && !alt)
        {
            if (!LegalMode)
            {
                _pk.SetUnshiny();
                RaiseAll();
                _status("Não é mais shiny.");
                return;
            }
            var before = _pk.Clone();
            _guardSuspended = true;
            _pk.SetUnshiny();
            _isNew = false;
            RaiseAll();
            LegalizeOrRestore(before);
            return;
        }
        if (IsShinyLocked && LegalMode)
        {
            _status($"Modo legal: {SpeciesName} vem de um encontro que nunca é shiny (shiny lock).");
            return;
        }
        if (_pk.Format <= 2)
        {
            MakeShiny(); // Gen 1/2: o shiny vem dos IVs
            return;
        }
        if (alt)
        {
            // Mantem o PID (e a ligacao PID/IV), muda o SID do treinador original.
            var oldSid = _pk.SID16;
            _pk.SetShinySID(type);
            _isNew = false;
            RaiseAll(); // no modo legal, o guarda desfaz se ficar ilegal
            if (_pk.IsShiny)
                _status(LegalityStatus($"Shiny mantendo o PID: o SID do treinador original mudou de {oldSid} para {_pk.SID16}"));
            return;
        }
        if (!LegalMode)
        {
            _pk.SetShiny(type);
            RaiseAll();
            _status($"Shiny{(type == Shiny.AlwaysSquare ? " quadrado" : type == Shiny.AlwaysStar ? " estrela" : "")} (PID trocado).");
            return;
        }
        // Modo legal: trocar o PID quebra a correlacao PID/IV; o Legalizar refaz pedindo o shiny (e o formato).
        var prev = _pk.Clone();
        _guardSuspended = true;
        _pk.SetShiny(type);
        _isNew = false;
        RaiseAll();
        LegalizeOrRestore(prev);
    }

    private void MakeShiny()
    {
        if (!LegalMode)
        {
            _pk.SetShiny();
            RaiseAll();
            return;
        }
        // Modo legal: shiny no PID costuma quebrar a correlacao PID/IV; o Legalizar refaz pedindo shiny.
        var before = _pk.Clone();
        _guardSuspended = true;
        _pk.SetShiny();
        _isNew = false;
        RaiseAll();
        LegalizeOrRestore(before);
    }

    // Encontro (modo legal: escolher outro encontro real em vez de mexer em local/nivel)
    public string CurrentEncounter { get; private set; } = "";
    private (ushort, byte)? _encounterKey;
    private IReadOnlyList<EncounterChoice> _encounterOptions = [];
    /// <summary>Encontros possiveis da especie/forma neste jogo (calculado so quando a aba Encontro aparece).</summary>
    public IReadOnlyList<EncounterChoice> EncounterOptions
    {
        get
        {
            var key = (_pk.Species, _pk.Form);
            if (_sav is null || CoreAdapter.IsEmpty(_pk))
                return [];
            if (key != _encounterKey)
            {
                _encounterKey = key;
                try
                {
                    _encounterOptions = [.. EncounterDatabase.SearchEncounters(_sav, _pk.Species, onlyThisGame: true)
                        .Where(e => e.Form == _pk.Form || e is MysteryGift)
                        .Take(80)
                        .Select(e => new EncounterChoice(EncounterDatabase.GetShortLabel(e), e))
                        .DistinctBy(c => c.Label)];
                }
                catch
                {
                    _encounterOptions = [];
                }
            }
            return _encounterOptions;
        }
    }
    /// <summary>Escolher um encontro na lista gera o Pokemon de novo a partir dele (mantendo natureza, nivel, golpes...).</summary>
    public EncounterChoice? SelectedEncounter
    {
        get => null;
        set
        {
            if (value is null)
                return;
            var before = _pk.Clone();
            _guardSuspended = true;
            _ = LegalizeAsync(before, value.Encounter);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Raise(nameof(SelectedEncounter)), Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    // Fitas
    public RelayCommand AddAllRibbonsCommand { get; }
    public RelayCommand RemoveAllRibbonsCommand { get; }
    private IReadOnlyList<RibbonViewModel>? _ribbons;
    /// <summary>Todas as fitas do formato (lista refeita quando o Pokemon muda por inteiro).</summary>
    private IReadOnlyList<RibbonViewModel> AllRibbons => _ribbons ??= [.. CoreAdapter.GetRibbons(_pk)
        .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
        .Select(r => new RibbonViewModel(() => _pk, r, OnRibbonChanged))];
    public bool HasRibbons => AllRibbons.Count > 0;
    private string _ribbonFilter = "";
    public string RibbonFilter { get => _ribbonFilter; set { if (Set(ref _ribbonFilter, value ?? "")) Raise(nameof(Ribbons)); } }
    private bool _onlyOwnedRibbons;
    public bool OnlyOwnedRibbons { get => _onlyOwnedRibbons; set { if (Set(ref _onlyOwnedRibbons, value)) Raise(nameof(Ribbons)); } }
    public IReadOnlyList<RibbonViewModel> Ribbons => [.. AllRibbons.Where(r =>
        (!_onlyOwnedRibbons || r.Has) && (_ribbonFilter.Length == 0 || r.Name.Contains(_ribbonFilter, StringComparison.CurrentCultureIgnoreCase)))];
    public string RibbonSummary => $"{AllRibbons.Count(r => r.Has)} de {AllRibbons.Count} fitas";

    private void OnRibbonChanged()
    {
        Refresh();
        Raise(nameof(RibbonSummary));
    }

    // Memorias (Gen 6+)
    public MemoryViewModel? OTMemory { get; }
    public MemoryViewModel? HTMemory { get; }
    public bool HasMemories => OTMemory is not null || HTMemory is not null;

    // Contest stats, Dynamax/Gigantamax, alpha e nobre
    public IReadOnlyList<ContestStatViewModel> ContestStats { get; }
    /// <summary>Modo legal: a secao some quando nenhum jogo da historia do Pokemon tem concursos (ex.: Scarlet/Violet).</summary>
    public bool HasContestStats => ContestStats.Count > 0 && (!LegalMode || ContestRule != CoreAdapter.ContestRule.None);
    private CoreAdapter.ContestRule ContestRule => CoreAdapter.GetContestRule(_pk).Rule;
    public string ContestNote => !LegalMode ? "Modo legal desligado: qualquer valor."
        : CoreAdapter.GetContestRule(_pk).Rule switch
        {
            CoreAdapter.ContestRule.Correlate => "O Sheen acompanha os atributos (Pokéblocks/Poffins): ao subir um atributo, ele é ajustado sozinho para o mínimo legal.",
            CoreAdapter.ContestRule.NoSheen => "Omega Ruby/Alpha Sapphire: atributos livres, Sheen sempre 0.",
            _ => "Qualquer valor de Sheen é legal para este Pokémon.",
        };

    /// <summary>
    /// Modo legal: ao mudar um atributo de concurso, o Sheen vai para a faixa legal (como no jogo, os Pokeblocks/Poffins
    /// sobem os dois juntos). Sem isso, subir so o Cool deixava o Pokemon ilegal e a mudanca era desfeita.
    /// </summary>
    private void OnContestChanged(int index)
    {
        if (LegalMode && index < 5 && _pk is IContestStats c)
        {
            var (rule, min, max) = CoreAdapter.GetContestRule(_pk);
            byte sheen = rule switch
            {
                CoreAdapter.ContestRule.NoSheen => 0,
                CoreAdapter.ContestRule.Correlate when min <= max => Math.Clamp(c.ContestSheen, min, max),
                _ => c.ContestSheen,
            };
            if (sheen != c.ContestSheen)
            {
                c.ContestSheen = sheen;
                ContestStats[5].RaiseAll();
            }
        }
        Refresh();
        Raise(nameof(ContestNote));
    }
    public bool HasDynamax => _pk is IDynamaxLevel;
    public int DynamaxLevel
    {
        get => _pk is IDynamaxLevel d ? d.DynamaxLevel : 0;
        set { if (_pk is IDynamaxLevel d) { d.DynamaxLevel = (byte)Math.Clamp(value, 0, 10); Refresh(); } }
    }
    public bool HasGigantamax => _pk is IGigantamax;
    public bool CanGigantamax
    {
        get => _pk is IGigantamax g && g.CanGigantamax;
        set { if (_pk is IGigantamax g) { g.CanGigantamax = value; Refresh(); } }
    }
    public bool HasAlpha => _pk is IAlpha;
    public bool IsAlpha
    {
        get => _pk is IAlpha a && a.IsAlpha;
        set { if (_pk is IAlpha a) { a.IsAlpha = value; Refresh(); } }
    }
    public bool HasNoble => _pk is INoble;
    public bool IsNoble
    {
        get => _pk is INoble n && n.IsNoble;
        set { if (_pk is INoble n) { n.IsNoble = value; Refresh(); } }
    }
    public bool HasSpecialFlags => HasDynamax || HasGigantamax || HasAlpha || HasNoble;

    // Marcacoes e Hyper Training
    public IReadOnlyList<MarkingViewModel> Markings { get; }
    public bool HasMarkings => Markings.Count > 0;
    public IReadOnlyList<HyperTrainViewModel> HyperTraining { get; }
    public bool HasHyperTraining => HyperTraining.Count > 0;
    private static readonly string[] StatLabelsShort = ["PS", "Atq", "Def", "AtE", "DeE", "Vel"];

    // PID / EC editaveis (hexadecimal, aplicado ao sair do campo)
    public string PIDText
    {
        get => $"{_pk.PID:X8}";
        set
        {
            if (uint.TryParse(value?.Trim(), System.Globalization.NumberStyles.HexNumber, null, out var v) && v != _pk.PID)
            {
                _pk.PID = v;
                RaiseAll();
            }
            else
                Raise();
        }
    }
    public string ECText
    {
        get => $"{_pk.EncryptionConstant:X8}";
        set
        {
            if (uint.TryParse(value?.Trim(), System.Globalization.NumberStyles.HexNumber, null, out var v) && v != _pk.EncryptionConstant)
            {
                _pk.EncryptionConstant = v;
                RaiseAll();
            }
            else
                Raise();
        }
    }
    /// <summary>Gen 3-5 nao tem EC proprio (vem do PID).</summary>
    public bool HasEC => _pk.Format >= 6;
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
    /// <summary>A Gen 1 nao guarda felicidade nos Pokemon.</summary>
    public bool HasFriendship => _pk.Format >= 2;
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
        foreach (var h in HyperTraining)
            h.RaiseAll();
        foreach (var mk in Markings)
            mk.RaiseAll();
        foreach (var c in ContestStats)
            c.RaiseAll();
        OTMemory?.RaiseAll();
        HTMemory?.RaiseAll();
        _ribbons = null; // a lista de fitas depende do Pokemon inteiro (troca de especie, Legalizar...)
        Raise(nameof(Ribbons));
        Raise(nameof(RibbonSummary));
        Raise(nameof(HasRibbons));
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
        Raise(nameof(FriendshipEvolutions));
        Raise(nameof(HasFriendshipEvolutions));
        Raise(nameof(BeautyEvolutions));
        Raise(nameof(HasBeautyEvolutions));
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
        RefreshPokerus();
        RaiseHiddenPower();
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
public sealed record MoveOption(int Index, string Name, bool IsLearnable, int Generation = 9, GameVersion Version = GameVersion.Any)
{
    /// <summary>Descricao em portugues e onde aprender por TM/tutor neste jogo (AllGenWiki), na lista de golpes.</summary>
    public string? Tip => MoveSlotViewModel.CombineTip(GameText.GetMove(Name, Generation), GameText.GetMoveWhere(Name, Generation, Version));
    public override string ToString() => Name;
}

/// <summary>Um encontro possivel na lista "Trocar encontro" (modo legal).</summary>
public sealed record EncounterChoice(string Label, IEncounterInfo Encounter)
{
    public override string ToString() => Label;
}

/// <summary>Uma marcacao (●▲■♥★◆). Gen 3-6: liga/desliga; Gen 7+: nenhuma → azul → rosa.</summary>
public sealed class MarkingViewModel(Func<PKM> pk, int index, Action changed) : ViewModelBase
{
    public void RaiseAll() => Raise(string.Empty);
    public string Symbol => CoreAdapter.GetMarkingSymbol(pk(), index);
    /// <summary>0 = desligada, 1 = ligada/azul, 2 = rosa.</summary>
    public int State => CoreAdapter.GetMarking(pk(), index);
    public bool IsOn => State == 1;
    public bool IsPink => State == 2;
    public RelayCommand ToggleCommand => new(() =>
    {
        CoreAdapter.CycleMarking(pk(), index);
        Raise(nameof(State)); Raise(nameof(IsOn)); Raise(nameof(IsPink));
        changed();
    });
}

/// <summary>Uma fita: liga/desliga ou contagem (fitas de concurso da Gen 3/4, memoria de batalha/concurso).</summary>
public sealed class RibbonViewModel(Func<PKM> pk, CoreAdapter.RibbonEntry ribbon, Action changed) : ViewModelBase
{
    private int _value = ribbon.Value;
    public string Name => ribbon.Name;
    public bool IsCount => ribbon.IsCount;
    public bool IsFlag => !ribbon.IsCount;
    public int Max => ribbon.Max;
    public bool Has => _value > 0;
    public bool IsOn
    {
        get => _value > 0;
        set => Count = value ? 1 : 0;
    }
    public int Count
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, 0, ribbon.Max);
            if (value == _value)
                return;
            CoreAdapter.SetRibbon(pk(), ribbon, value);
            _value = value;
            Raise(); Raise(nameof(IsOn)); Raise(nameof(Has));
            changed();
        }
    }
}

/// <summary>Memoria do treinador original ou do atual (Gen 6+): memoria, intensidade, sentimento e o argumento.</summary>
public sealed class MemoryViewModel : ViewModelBase
{
    private readonly Func<PKM> _pk;
    private readonly bool _ot;
    private readonly Action _changed;

    public MemoryViewModel(Func<PKM> pk, bool originalTrainer, Action changed)
    {
        _pk = pk;
        _ot = originalTrainer;
        _changed = changed;
        // Os textos do jogo tem marcadores ({0} = Pokemon, {1} = treinador, {2} = detalhe...): troca por palavras.
        Memories = [.. CoreAdapter.MemoryTexts.Memory.Select(m => new ComboItem(CleanMemory(m.Text), m.Value))];
        Intensities = [.. CoreAdapter.MemoryTexts.GetMemoryQualities().ToArray()];
    }

    public string Title => _ot ? "Treinador original" : "Treinador atual";

    private static string CleanMemory(string text) => text
        .Replace("{0}", "the Pokémon").Replace("{1}", "the Trainer").Replace("{2}", "[detalhe]")
        .Replace("{3}", "[sentimento]").Replace("{4}", "[intensidade]");
    public IReadOnlyList<ComboItem> Memories { get; }
    public IReadOnlyList<string> Intensities { get; }
    public IReadOnlyList<string> Feelings => [.. CoreAdapter.MemoryTexts.GetMemoryFeelings(Gen).ToArray()];
    public IReadOnlyList<ComboItem> Arguments => CoreAdapter.GetMemoryArguments(MemoryId, Gen);
    public bool HasArgument => Arguments.Count > 1;
    private int Gen => CoreAdapter.GetMemoryGen(_pk(), _ot);

    private byte MemoryId
    {
        get => _pk() switch { IMemoryOT o when _ot => o.OriginalTrainerMemory, IMemoryHT h when !_ot => h.HandlingTrainerMemory, _ => 0 };
        set
        {
            if (_ot && _pk() is IMemoryOT o) o.OriginalTrainerMemory = value;
            else if (!_ot && _pk() is IMemoryHT h) h.HandlingTrainerMemory = value;
        }
    }

    public ComboItem? Memory
    {
        get => Memories.FirstOrDefault(m => m.Value == MemoryId);
        set
        {
            if (value is null || value.Value == MemoryId)
                return;
            MemoryId = (byte)value.Value;
            Argument = null;
            Raise(); Raise(nameof(Arguments)); Raise(nameof(HasArgument)); Raise(nameof(Argument));
            _changed();
        }
    }

    public int Intensity
    {
        get => _pk() switch { IMemoryOT o when _ot => o.OriginalTrainerMemoryIntensity, IMemoryHT h when !_ot => h.HandlingTrainerMemoryIntensity, _ => 0 };
        set
        {
            if (value < 0) return;
            if (_ot && _pk() is IMemoryOT o) o.OriginalTrainerMemoryIntensity = (byte)value;
            else if (!_ot && _pk() is IMemoryHT h) h.HandlingTrainerMemoryIntensity = (byte)value;
            Raise(); _changed();
        }
    }

    public int Feeling
    {
        get => _pk() switch { IMemoryOT o when _ot => o.OriginalTrainerMemoryFeeling, IMemoryHT h when !_ot => h.HandlingTrainerMemoryFeeling, _ => 0 };
        set
        {
            if (value < 0) return;
            if (_ot && _pk() is IMemoryOT o) o.OriginalTrainerMemoryFeeling = (byte)value;
            else if (!_ot && _pk() is IMemoryHT h) h.HandlingTrainerMemoryFeeling = (byte)value;
            Raise(); _changed();
        }
    }

    private ushort Variable
    {
        get => _pk() switch { IMemoryOT o when _ot => o.OriginalTrainerMemoryVariable, IMemoryHT h when !_ot => h.HandlingTrainerMemoryVariable, _ => 0 };
        set
        {
            if (_ot && _pk() is IMemoryOT o) o.OriginalTrainerMemoryVariable = value;
            else if (!_ot && _pk() is IMemoryHT h) h.HandlingTrainerMemoryVariable = value;
        }
    }

    public ComboItem? Argument
    {
        get => Arguments.FirstOrDefault(a => a.Value == Variable);
        set
        {
            var v = (ushort)(value?.Value ?? 0);
            if (v == Variable)
                return;
            Variable = v;
            Raise();
            _changed();
        }
    }

    public void RaiseAll() => Raise(string.Empty);
}

/// <summary>Contest stat (Gen 3+): Cool, Beauty, Cute, Smart, Tough e Sheen.</summary>
public sealed class ContestStatViewModel(Func<PKM> pk, int index, Action changed) : ViewModelBase
{
    private static readonly string[] Labels = ["Cool", "Beauty", "Cute", "Smart", "Tough", "Sheen"];
    public string Label => Labels[index];
    public int Value
    {
        get => pk() is IContestStats c ? index switch
        {
            0 => c.ContestCool, 1 => c.ContestBeauty, 2 => c.ContestCute, 3 => c.ContestSmart, 4 => c.ContestTough, _ => c.ContestSheen,
        } : 0;
        set
        {
            if (pk() is not IContestStats c)
                return;
            var v = (byte)Math.Clamp(value, 0, 255);
            switch (index)
            {
                case 0: c.ContestCool = v; break;
                case 1: c.ContestBeauty = v; break;
                case 2: c.ContestCute = v; break;
                case 3: c.ContestSmart = v; break;
                case 4: c.ContestTough = v; break;
                default: c.ContestSheen = v; break;
            }
            Raise();
            changed();
        }
    }
    public void RaiseAll() => Raise(string.Empty);
}

/// <summary>Hyper Training de um atributo (Gen 7+): conta como IV 31 nos atributos.</summary>
public sealed class HyperTrainViewModel(Func<PKM> pk, int index, string label, Action changed) : ViewModelBase
{
    public string Label { get; } = label;
    public bool IsOn
    {
        get => pk() is IHyperTrain h && CoreAdapter.GetHyperTrain(h, index);
        set
        {
            if (pk() is not IHyperTrain h || value == IsOn)
                return;
            CoreAdapter.SetHyperTrain(h, index, value);
            Raise();
            changed();
        }
    }
    public void RaiseAll() => Raise(string.Empty);
}

public sealed class MoveSlotViewModel(Func<PKM> pk, int index, Func<IReadOnlyList<MoveOption>> options, Func<GameVersion>? version = null) : ViewModelBase
{
    /// <summary>Descricao + "Onde aprender" (TM/tutor), para tooltips.</summary>
    public static string? CombineTip(string? desc, string? where)
        => where is null ? desc : (desc is null ? "" : desc + "\n\n") + "Onde aprender:\n" + where;

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
    /// <summary>Descricao do golpe em portugues (AllGenWiki), quando houver.</summary>
    public string? Tip => HasMove ? GameText.GetMove(CoreAdapter.MoveNames[Move], pk().Format) : null;
    /// <summary>Descricao e onde aprender por TM/tutor no jogo do save (tooltip do campo).</summary>
    public string? FullTip => HasMove
        ? CombineTip(Tip, GameText.GetMoveWhere(CoreAdapter.MoveNames[Move], pk().Format, version?.Invoke() ?? pk().Version))
        : "Golpes em verde: aprende oficialmente (nível, TM, tutor, ovo ou encontro)";
    public RelayCommand ClearCommand => new(() => Move = 0);
    private (string Name, uint Argb)? Type => Move == (int)PKHeX.Core.Move.HiddenPower && pk().Format is >= 2 and <= 7
        ? (CoreAdapter.GetHiddenPowerType(pk()).Name, CoreAdapter.GetHiddenPowerType(pk()).Argb)
        : CoreAdapter.GetMoveType((ushort)Move, pk().Context);
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
/// <summary>Uma forma no seletor, com o sprite dela (mesmo shiny e genero do Pokemon).</summary>
public sealed record FormOption(string Text, int Value, ushort Species, bool Shiny, byte Gender, EntityContext Context)
{
    public Avalonia.Media.Imaging.Bitmap? Sprite => SpriteService.GetSpeciesSprite(Species, Shiny, (byte)Value, Gender, Context);
    public override string ToString() => Text;
}

public sealed class FriendshipEvolutionOption(CoreAdapter.FriendshipEvolution evo, RelayCommand command)
{
    public string Label => $"Evoluir para {evo.Name}";
    public string Requirement => evo.Requirement;
    public bool IsBlocked => evo.Blocked is not null;
    public string Tooltip => evo.Blocked is { } why ? $"Não dá: {why}." : $"Simula {evo.Requirement}. Para dia/noite, o botão escolhe o período da evolução. Aplicar ou Salvar grava o resultado.";
    public RelayCommand Command { get; } = command;
}

public sealed class ItemEvolutionOption(CoreAdapter.ItemEvolution evo, RelayCommand command, bool isBeauty = false)
{
    public string Label => $"Evoluir para {evo.Name}";
    public string Requirement => evo.Requirement;
    public bool IsBlocked => evo.Blocked is not null;
    public string Tooltip => evo.Blocked is { } why ? $"Não dá: {why}." : isBeauty ? $"Simula {evo.Requirement}. Aplicar ou Salvar grava o resultado." : $"Simula o uso do item ({evo.Requirement}). Clique em Aplicar para gravar.";
    public RelayCommand Command { get; } = command;
}

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
