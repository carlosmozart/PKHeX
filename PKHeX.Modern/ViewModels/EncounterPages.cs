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
        SetMatches([]);
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
            SetMatches([.. found.Select(e => new EncounterCardViewModel(e))]);
            Summary = found.Count == 0
                ? $"Nenhum encontro de {CoreAdapter.SpeciesNames[species]}" + (only ? " neste jogo. Desmarque “Só deste jogo” para ver outros jogos." : ".")
                : $"{found.Count} encontro(s) de {CoreAdapter.SpeciesNames[species]}. Clique em Usar para levar ao editor.";
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
    public string Search { get => _search; set { if (Set(ref _search, value)) ApplyFilter(); } }

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
            ApplyFilter();
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

    private void ApplyFilter()
    {
        var q = _search.Trim();
        var matches = q.Length == 0 ? _all : [.. _all.Where(c => c.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase))];
        SetMatches(matches);
        Summary = _all.Count == 0 ? "Nenhum evento disponível para este jogo."
            : $"{matches.Count} de {_all.Count} evento(s)" + (q.Length > 0 ? $" para “{q}”" : "") + ". Clique em Usar para levar ao editor.";
    }
}
