using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>Cartao de um encontro ou Mystery Gift (compartilhado pelas duas paginas).</summary>
public sealed class EncounterCardViewModel(IEncounterInfo enc) : ViewModelBase
{
    public IEncounterInfo Encounter { get; } = enc;

    public string Title => EncounterDatabase.GetSpeciesFormName(Encounter.Species, Encounter.Form, Encounter.Context);
    /// <summary>Tipo do encontro (ex.: "Wild Encounter") ou titulo do card do evento.</summary>
    public string Kind => Encounter switch
    {
        MysteryGift g when !string.IsNullOrWhiteSpace(g.CardTitle) => g.CardTitle.Replace('　', ' ').Trim(),
        IEncounterable e => e.LongName,
        _ => "",
    };
    public string Level => Encounter.LevelMin == Encounter.LevelMax ? $"Nv. {Encounter.LevelMin}" : $"Nv. {Encounter.LevelMin}–{Encounter.LevelMax}";
    public string Location => EncounterDatabase.GetLocationName(Encounter);
    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);
    public string Game => $"{EncounterDatabase.GetVersionName(Encounter.Version)} · Gen {Encounter.Generation}";
    public bool IsEgg => Encounter.IsEgg;
    public bool IsShiny => Encounter.IsShiny;
    public string Details => EncounterDatabase.GetDetails(Encounter);
    public string SearchText => $"{Title} {Kind} {Location} {Game}";
    /// <summary>Selvagem, Estático, Troca, Ovo, Raid, Evento... (filtro por tipo).</summary>
    public string Category { get; } = EncounterDatabase.GetCategory(enc);
    public string VersionName => EncounterDatabase.GetVersionName(Encounter.Version);
    public bool IsHomeGift => EncounterDatabase.IsHomeGift(Encounter);
    public bool IsShinyLocked => Encounter.Shiny == Shiny.Never;
    private IReadOnlyList<ushort>? _moves;
    /// <summary>Golpes que ja vem com o encontro (filtro por golpe e painel de detalhes).</summary>
    public IReadOnlyList<ushort> FixedMoves => _moves ??= EncounterDatabase.GetFixedMoves(Encounter);

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private Bitmap? _sprite;
    private bool _spriteLoaded;
    /// <summary>Sprite gerado so quando o cartao aparece.</summary>
    public Bitmap? Sprite
    {
        get
        {
            if (!_spriteLoaded)
            {
                _spriteLoaded = true;
                try { _sprite = SpriteService.GetSprite(Encounter); } catch { _sprite = null; }
            }
            return _sprite;
        }
    }
}

/// <summary>Base das paginas de banco: lista de cartoes com "mostrar mais" e acao "Usar".</summary>
public abstract class EncounterListPageViewModel : PageViewModel
{
    private const int PageSize = 90;
    private IReadOnlyList<EncounterCardViewModel> _matches = [];

    protected EncounterListPageViewModel(Action<IEncounterInfo> use)
    {
        UseCommand = new RelayCommand(p => { if (p is EncounterCardViewModel c) use(c.Encounter); });
        ShowMoreCommand = new RelayCommand(() => ShowCount(Results.Count + PageSize));
        SelectCommand = new RelayCommand(p => Selected = p as EncounterCardViewModel);
        CloseDetailCommand = new RelayCommand(() => Selected = null);
        ClearFiltersCommand = new RelayCommand(() => { _kind = All; _version = All; _move = ""; RaiseFilters(); ApplyFilters(); });
    }

    // Selecao e painel de detalhes
    public RelayCommand SelectCommand { get; }
    public RelayCommand CloseDetailCommand { get; }
    private EncounterCardViewModel? _selected;
    /// <summary>Cartao clicado: abre o painel de detalhes a direita.</summary>
    public EncounterCardViewModel? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            if (_selected is not null)
                _selected.IsSelected = false;
            _selected = value;
            if (value is not null)
                value.IsSelected = true;
            Detail = value is null ? null : new EncounterDetailViewModel(value);
            Raise();
            Raise(nameof(Detail));
            Raise(nameof(HasDetail));
        }
    }
    public EncounterDetailViewModel? Detail { get; private set; }
    public bool HasDetail => Detail is not null;

    // Filtros (tipo, versao, golpe) sobre o resultado da busca
    protected const string All = "Todos";
    private IReadOnlyList<EncounterCardViewModel> _source = [];
    public RelayCommand ClearFiltersCommand { get; }
    public IReadOnlyList<string> KindOptions { get; private set; } = [All];
    public IReadOnlyList<string> VersionOptions { get; private set; } = [All];
    public IReadOnlyList<string> MoveNames => CoreAdapter.MoveNames;
    private string _kind = All, _version = All, _move = "";
    public string KindFilter { get => _kind; set { if (Set(ref _kind, value ?? All)) ApplyFilters(); } }
    public string VersionFilter { get => _version; set { if (Set(ref _version, value ?? All)) ApplyFilters(); } }
    /// <summary>So encontros que ja vem com este golpe (texto parcial e ignorado ate virar um golpe).</summary>
    public string MoveFilter
    {
        get => _move;
        set
        {
            if (!Set(ref _move, value ?? ""))
                return;
            if (_move.Length == 0 || CoreAdapter.FindIndex(CoreAdapter.MoveNames, _move) > 0)
                ApplyFilters();
        }
    }
    public bool HasFilters => _source.Count > 0;
    public bool IsFiltered => _kind != All || _version != All || _move.Length > 0;

    private void RaiseFilters()
    {
        foreach (var p in (string[])[nameof(KindFilter), nameof(VersionFilter), nameof(MoveFilter)])
            Raise(p);
    }

    /// <summary>Novo resultado de busca: refaz as opcoes dos filtros e aplica.</summary>
    protected void SetSource(IReadOnlyList<EncounterCardViewModel> cards)
    {
        _source = cards;
        KindOptions = [All, .. cards.Select(c => c.Category).Distinct().Order()];
        VersionOptions = [All, .. cards.Select(c => c.VersionName).Where(v => v.Length > 0).Distinct().Order()];
        if (!KindOptions.Contains(_kind)) _kind = All;
        if (!VersionOptions.Contains(_version)) _version = All;
        foreach (var p in (string[])[nameof(KindOptions), nameof(VersionOptions), nameof(HasFilters)])
            Raise(p);
        RaiseFilters();
        ApplyFilters();
    }

    /// <summary>Filtro extra da pagina (ex.: texto da busca de eventos).</summary>
    protected virtual bool Matches(EncounterCardViewModel card) => true;

    /// <summary>Resumo depois de filtrar (cada pagina escreve o seu).</summary>
    protected virtual void OnFiltered(int shown, int total) { }

    protected void ApplyFilters()
    {
        var move = _move.Length == 0 ? 0 : CoreAdapter.FindIndex(CoreAdapter.MoveNames, _move);
        var list = _source.Where(c => (_kind == All || c.Category == _kind)
                                      && (_version == All || c.VersionName == _version)
                                      && (move <= 0 || c.FixedMoves.Contains((ushort)move))
                                      && Matches(c)).ToList();
        SetMatches(list);
        if (Selected is { } sel && !list.Contains(sel))
            Selected = null;
        Raise(nameof(IsFiltered));
        OnFiltered(list.Count, _source.Count);
    }

    protected SaveFile? Sav { get; private set; }

    public RelayCommand UseCommand { get; }
    public RelayCommand ShowMoreCommand { get; }
    public ObservableCollection<EncounterCardViewModel> Results { get; } = [];

    private bool _onlyThisGame = true;
    /// <summary>"Só deste jogo": o filtro "unavailable species" do PKHeX.</summary>
    public bool OnlyThisGame { get => _onlyThisGame; set { if (Set(ref _onlyThisGame, value)) OnOnlyThisGameChanged(); } }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; protected set => Set(ref _isBusy, value); }

    private string _summary = "";
    public string Summary { get => _summary; protected set => Set(ref _summary, value); }

    public bool HasMore => Results.Count < _matches.Count;

    public override void Load(SaveFile sav)
    {
        Sav = sav;
        Selected = null;
        _source = [];
        SetMatches([]);
        Raise(nameof(HasFilters));
        Summary = "";
    }

    protected virtual void OnOnlyThisGameChanged() { }

    protected void SetMatches(IReadOnlyList<EncounterCardViewModel> matches)
    {
        _matches = matches;
        Results.Clear();
        ShowCount(PageSize);
    }

    private void ShowCount(int count)
    {
        for (int i = Results.Count; i < Math.Min(count, _matches.Count); i++)
            Results.Add(_matches[i]);
        Raise(nameof(HasMore));
    }
}

/// <summary>Banco de encontros: todos os jeitos de obter uma especie (selvagem, estatico, troca, ovo, evento).</summary>
public sealed class EncounterDbViewModel : EncounterListPageViewModel
{
    private CancellationTokenSource? _cts;

    public EncounterDbViewModel(Action<IEncounterInfo> use) : base(use)
        => SearchCommand = new RelayCommand(() => _ = SearchAsync());

    public override string Title => "Encontros";
    public override string Icon => "🌿";

    public RelayCommand SearchCommand { get; }
    public IReadOnlyList<string> SpeciesNames => CoreAdapter.SpeciesNames;
    private string _speciesName = "";
    private bool _onlyGame;

    protected override void OnFiltered(int shown, int total)
    {
        Summary = total == 0
            ? $"Nenhum encontro de {_speciesName}" + (_onlyGame ? " neste jogo. Desmarque “Só deste jogo” para ver outros jogos." : ".")
            : (shown == total ? $"{total} encontro(s) de {_speciesName}." : $"{shown} de {total} encontro(s) de {_speciesName} com os filtros.")
              + " Clique num cartão para ver os detalhes; Usar leva ao editor.";
    }

    private string? _species;
    /// <summary>Nome da especie digitado/escolhido (busca automatica ao escolher).</summary>
    public string? Species
    {
        get => _species;
        set
        {
            if (Set(ref _species, value) && GetSpeciesId() > 0)
                _ = SearchAsync();
        }
    }

    protected override void OnOnlyThisGameChanged()
    {
        if (GetSpeciesId() > 0)
            _ = SearchAsync();
    }

    private ushort GetSpeciesId()
    {
        if (string.IsNullOrWhiteSpace(_species))
            return 0;
        var names = CoreAdapter.SpeciesNames;
        for (int i = 1; i < names.Count; i++)
        {
            if (string.Equals(names[i], _species.Trim(), StringComparison.OrdinalIgnoreCase))
                return (ushort)i;
        }
        return 0;
    }

    public async Task SearchAsync()
    {
        if (Sav is not { } sav)
            return;
        var species = GetSpeciesId();
        if (species == 0)
        {
            Summary = "Escolha uma espécie para buscar.";
            return;
        }
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsBusy = true;
        Summary = $"Buscando encontros de {CoreAdapter.SpeciesNames[species]}...";
        try
        {
            bool only = OnlyThisGame;
            var found = await Task.Run(() => EncounterDatabase.SearchEncounters(sav, species, only, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
                return;
            _speciesName = CoreAdapter.SpeciesNames[species];
            _onlyGame = only;
            Selected = null;
            SetSource([.. found.Select(e => new EncounterCardViewModel(e))]);
        }
        catch (OperationCanceledException)
        {
            // substituida por uma busca mais nova
        }
        catch (Exception ex)
        {
            Summary = $"Erro na busca: {ex.Message}";
        }
        finally
        {
            if (_cts == cts)
                IsBusy = false;
        }
    }
}

/// <summary>Banco de Mystery Gift (eventos), com busca por texto.</summary>
public sealed class GiftDbViewModel(Action<IEncounterInfo> use) : EncounterListPageViewModel(use)
{
    private IReadOnlyList<EncounterCardViewModel> _all = [];
    private bool _loaded;

    public override string Title => "Eventos";
    public override string Icon => "🎁";

    private string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value)) ApplyFilters(); } }

    public override void Load(SaveFile sav)
    {
        base.Load(sav);
        _all = [];
        _loaded = false; // carrega ao entrar na pagina (o banco tem milhares de eventos)
    }

    protected override void OnOnlyThisGameChanged()
    {
        _loaded = false;
        _ = EnsureLoadedAsync();
    }

    /// <summary>Le o banco em segundo plano na primeira vez que a pagina e aberta.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded || IsBusy || Sav is not { } sav)
            return;
        IsBusy = true;
        Summary = "Carregando eventos...";
        try
        {
            bool only = OnlyThisGame;
            var gifts = await Task.Run(() => EncounterDatabase.LoadGifts(sav, only));
            _all = [.. gifts.Select(g => new EncounterCardViewModel(g))];
            _loaded = true;
            SetSource(_all);
        }
        catch (Exception ex)
        {
            Summary = $"Erro ao carregar eventos: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected override bool Matches(EncounterCardViewModel card)
    {
        var q = _search.Trim();
        return q.Length == 0 || card.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    protected override void OnFiltered(int shown, int total)
    {
        var q = _search.Trim();
        Summary = total == 0 ? "Nenhum evento disponível para este jogo."
            : $"{shown} de {total} evento(s)" + (q.Length > 0 ? $" para “{q}”" : "") + ". Clique num cartão para ver os detalhes; Usar leva ao editor.";
    }
}

/// <summary>Painel de detalhes do encontro/evento selecionado: o que ele garante, golpes e o texto completo do PKHeX.</summary>
public sealed class EncounterDetailViewModel(EncounterCardViewModel card)
{
    public EncounterCardViewModel Card { get; } = card;
    public IReadOnlyList<EncounterFact> Facts { get; } = [.. EncounterDatabase.GetFacts(card.Encounter).Select(f => new EncounterFact(f.Label, f.Value))];
    public IReadOnlyList<EncounterMoveViewModel> Moves { get; } = [.. card.FixedMoves.Select(m => new EncounterMoveViewModel(m, card.Encounter))];
    public bool HasMoves => Moves.Count > 0;
    public string MovesHint => HasMoves ? "" : "Vem com os golpes do nível em que é encontrado.";
    public Bitmap? BallIcon => Card.Encounter is IFixedBall { FixedBall: not Ball.None } b ? SpriteService.GetBallSprite((byte)b.FixedBall) : null;
    public string Details => Card.Details;
}

public sealed record EncounterFact(string Label, string Value);

/// <summary>Golpe fixo do encontro, com o tipo e a descricao em portugues.</summary>
public sealed class EncounterMoveViewModel(ushort move, IEncounterInfo enc)
{
    private readonly (string Name, uint Argb)? _type = CoreAdapter.GetMoveType(move, enc.Context);
    public string Name => move < CoreAdapter.MoveNames.Count ? CoreAdapter.MoveNames[move] : $"#{move}";
    public string TypeName => _type?.Name ?? "";
    public Avalonia.Media.IBrush TypeBrush => new Avalonia.Media.SolidColorBrush(_type?.Argb ?? 0x00000000);
    public string? Tip => GameText.GetMove(Name, enc.Generation);
}
