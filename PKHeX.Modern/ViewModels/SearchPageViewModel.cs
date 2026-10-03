using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Pesquisa (banco de dados): todos os Pokemon dos saves abertos, dos saves da pasta e do bank numa lista so,
/// com filtros (texto, origem, shiny, geracao, nivel, IVs, natureza, bola) e ordenacao. Clicar num resultado abre
/// o save e vai ate o slot (ou abre a caixa do bank).
/// </summary>
public sealed class SearchPageViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private readonly Func<IReadOnlyList<(string Path, SaveFile Sav)>> _openSaves;
    private readonly Func<DbEntry, Task> _open;
    private readonly Dictionary<string, (DateTime Write, SaveFile Sav)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private List<DbEntry> _all = [];

    /// <param name="openSaves">Saves abertos nas abas (com as alteracoes nao salvas).</param>
    /// <param name="open">Abre um resultado (troca/abre a aba e vai ao slot, ou abre a caixa do bank).</param>
    public SearchPageViewModel(AppSettings settings, Func<IReadOnlyList<(string Path, SaveFile Sav)>> openSaves, Func<DbEntry, Task> open)
    {
        _settings = settings;
        _openSaves = openSaves;
        _open = open;
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsBusy);
        ClearCommand = new RelayCommand(ClearFilters);
        OpenCommand = new RelayCommand(p => { if (p is DbResultViewModel r) _ = _open(r.Entry); });
        SetGenerationCommand = new RelayCommand(p => GenerationFilter = p is int g && g != GenerationFilter ? g : null);
        Generations = [.. Enumerable.Range(1, 9).Select(g => new GenerationFilterViewModel(g))];
        NatureOptions = ["Todas as naturezas", .. CoreAdapter.NatureNames.Take(25)];
        BallOptions = ["Todas as bolas", .. GameInfo.Strings.balllist.Skip(1).Where(b => b.Length > 0).Distinct()];
    }

    public override string Title => "Pesquisa";
    public override string Icon => "🔎";
    public override void Load(SaveFile sav) { }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand SetGenerationCommand { get; }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); RefreshCommand.NotifyCanExecuteChanged(); } }
    public string Summary { get; private set; } = "";
    public string CountText { get; private set; } = "";
    public IReadOnlyList<DbResultViewModel> Results { get; private set; } = [];
    public bool HasNoResults => !IsBusy && Results.Count == 0;

    // Filtros
    private string _query = "";
    /// <summary>Palavras separadas por espaco; todas precisam aparecer (espécie, apelido, golpe, item, habilidade, natureza, bola, treinador, jogo, lugar). "shiny" e "ovo" também valem.</summary>
    public string Query { get => _query; set { if (Set(ref _query, value ?? "")) ApplyFilter(); } }

    public IReadOnlyList<string> SourceOptions { get; private set; } = ["Todos os saves e o bank"];
    private IReadOnlyList<DbSource> _sources = [];
    private int _source;
    public int SourceIndex { get => _source; set { if (value >= 0 && Set(ref _source, value)) ApplyFilter(); } }

    public IReadOnlyList<string> ShinyOptions { get; } = ["Shiny ou não", "Só shiny", "Sem shiny"];
    private int _shiny;
    public int ShinyIndex { get => _shiny; set { if (Set(ref _shiny, value)) ApplyFilter(); } }

    public IReadOnlyList<string> IvOptions { get; } = ["Qualquer IV", "6 IVs perfeitos", "5+ IVs perfeitos", "4+ IVs perfeitos", "3+ IVs perfeitos"];
    private int _iv;
    public int IvIndex { get => _iv; set { if (Set(ref _iv, value)) ApplyFilter(); } }

    public IReadOnlyList<string> NatureOptions { get; }
    private int _nature;
    public int NatureIndex { get => _nature; set { if (Set(ref _nature, value)) ApplyFilter(); } }

    public IReadOnlyList<string> BallOptions { get; }
    private int _ball;
    public int BallIndex { get => _ball; set { if (Set(ref _ball, value)) ApplyFilter(); } }

    public IReadOnlyList<string> SortOptions { get; } = ["Ordem: Pokédex", "Ordem: nível (maior)", "Ordem: IVs (maior)", "Ordem: nome", "Ordem: onde está"];
    private int _sort;
    public int SortIndex { get => _sort; set { if (Set(ref _sort, value)) ApplyFilter(); } }

    private decimal? _minLevel, _maxLevel;
    public decimal? MinLevel { get => _minLevel; set { if (Set(ref _minLevel, value)) ApplyFilter(); } }
    public decimal? MaxLevel { get => _maxLevel; set { if (Set(ref _maxLevel, value)) ApplyFilter(); } }

    private bool _includeEggs;
    public bool IncludeEggs { get => _includeEggs; set { if (Set(ref _includeEggs, value)) ApplyFilter(); } }

    public IReadOnlyList<GenerationFilterViewModel> Generations { get; }
    private int? _generation;
    /// <summary>Geracao de origem (onde o Pokemon foi capturado ou chocado).</summary>
    public int? GenerationFilter
    {
        get => _generation;
        set
        {
            if (!Set(ref _generation, value))
                return;
            foreach (var g in Generations)
                g.IsActive = g.Generation == value;
            ApplyFilter();
        }
    }

    private void ClearFilters()
    {
        _query = ""; _source = 0; _shiny = 0; _iv = 0; _nature = 0; _ball = 0; _minLevel = null; _maxLevel = null; _includeEggs = false; _generation = null;
        foreach (var g in Generations)
            g.IsActive = false;
        foreach (var p in (string[])[nameof(Query), nameof(SourceIndex), nameof(ShinyIndex), nameof(IvIndex), nameof(NatureIndex), nameof(BallIndex), nameof(MinLevel), nameof(MaxLevel), nameof(IncludeEggs), nameof(GenerationFilter)])
            Raise(p);
        ApplyFilter();
    }

    /// <summary>Le tudo de novo em segundo plano (chamado ao abrir a pagina). Saves da pasta que nao mudaram vem do cache.</summary>
    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Summary = "Lendo saves e bank...";
        Raise(nameof(Summary));
        Raise(nameof(HasNoResults));
        try
        {
            var open = _openSaves();
            var folder = _settings.SavesFolder ?? SaveLibrary.DefaultFolder;
            BankStorage.ExternalFolders = _settings.ExternalBankFolders;
            _all = await Task.Run(() => PokemonDatabase.Build(open, folder, _cache));

            var keep = _source > 0 && _source <= _sources.Count ? _sources[_source - 1].Id : null;
            _sources = [.. _all.Select(e => e.Source).DistinctBy(s => s.Id)];
            SourceOptions = ["Todos os saves e o bank", .. _sources.Select(s => s.Name)];
            Raise(nameof(SourceOptions));
            _source = keep is null ? 0 : Math.Max(0, _sources.ToList().FindIndex(s => s.Id == keep) + 1);
            Raise(nameof(SourceIndex));

            int saves = _sources.Count(s => !s.IsBank), banks = _sources.Count(s => s.IsBank);
            Summary = $"{_all.Count} Pokémon em {saves} save(s)" + (banks > 0 ? $" e {banks} banco(s) do bank" : "");
            Raise(nameof(Summary));
        }
        catch (Exception ex)
        {
            Summary = $"Erro ao ler os saves: {ex.Message}";
            Raise(nameof(Summary));
        }
        finally
        {
            IsBusy = false;
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        var source = _source > 0 && _source <= _sources.Count ? _sources[_source - 1] : null;
        var words = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nature = _nature > 0 ? NatureOptions[_nature] : null;
        var ball = _ball > 0 ? BallOptions[_ball] : null;
        int minIv = _iv == 0 ? 0 : 7 - _iv;
        IEnumerable<DbEntry> q = _all.Where(e =>
            (source is null || e.Source.Id == source.Id)
            && (_includeEggs || !e.Pkm.IsEgg)
            && (_shiny == 0 || e.Pkm.IsShiny == (_shiny == 1))
            && (_generation is null || e.Pkm.Generation == _generation)
            && (_minLevel is null || e.Pkm.CurrentLevel >= _minLevel)
            && (_maxLevel is null || e.Pkm.CurrentLevel <= _maxLevel)
            && e.PerfectIVs >= minIv
            && (nature is null || e.Nature == nature)
            && (ball is null || e.Ball == ball)
            && words.All(w => Matches(e, w)));
        q = _sort switch
        {
            1 => q.OrderByDescending(e => e.Pkm.CurrentLevel).ThenBy(e => e.Pkm.Species),
            2 => q.OrderByDescending(e => e.PerfectIVs).ThenByDescending(e => e.IvTotal),
            3 => q.OrderBy(e => e.Species, StringComparer.CurrentCultureIgnoreCase),
            4 => q, // ordem de leitura: aberto, pasta, bank
            _ => q.OrderBy(e => e.Pkm.Species).ThenBy(e => e.Pkm.Form),
        };
        var list = q.ToList();
        var old = Results.ToDictionary(r => r.Entry);
        Results = [.. list.Select(e => old.TryGetValue(e, out var r) ? r : new DbResultViewModel(e))];
        CountText = list.Count == _all.Count ? $"{list.Count} Pokémon" : $"{list.Count} de {_all.Count} Pokémon";
        Raise(nameof(Results));
        Raise(nameof(CountText));
        Raise(nameof(HasNoResults));
    }

    private static bool Matches(DbEntry e, string word)
    {
        if (word.Equals("shiny", StringComparison.OrdinalIgnoreCase))
            return e.Pkm.IsShiny;
        if (word.Equals("ovo", StringComparison.OrdinalIgnoreCase) || word.Equals("egg", StringComparison.OrdinalIgnoreCase))
            return e.Pkm.IsEgg;
        if (word.StartsWith('#') && int.TryParse(word[1..], out var dex))
            return e.Pkm.Species == dex;
        return e.Text.Contains(word, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Uma linha da pesquisa. O sprite so e montado quando a linha aparece na tela.</summary>
public sealed class DbResultViewModel(DbEntry entry)
{
    public DbEntry Entry { get; } = entry;
    private Bitmap? _sprite;
    private bool _loaded;
    public Bitmap? Sprite
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                try { _sprite = SpriteService.GetSprite(Entry.Pkm); } catch { _sprite = null; }
            }
            return _sprite;
        }
    }

    public string Name => Entry.Pkm.IsEgg ? $"Ovo ({Entry.Species})" : Entry.Nickname.Length > 0 ? $"{Entry.Nickname} ({Entry.Species})" : Entry.Species;
    public string LevelText => $"Nv. {Entry.Pkm.CurrentLevel} {CoreAdapter.GetGenderSymbol(Entry.Pkm)}".Trim();
    public bool IsShiny => Entry.Pkm.IsShiny;
    public string Details => string.Join(" · ", new[] { Entry.Nature, Entry.Ability, Entry.Item.Length > 0 ? $"@ {Entry.Item}" : "", Entry.Ball }.Where(s => s.Length > 0));
    public string MovesText => string.Join(" · ", Entry.Moves);
    public string IvText => $"IVs {Entry.IvTotal} · {Entry.PerfectIVs}× máx.";
    public string OriginText => $"{Entry.Origin} · {Entry.OT}";
    public string SourceText => Entry.Source.Name;
    public string WhereText => Entry.Where;
    public string OpenTip => Entry.Source.IsBank ? "Abrir esta caixa do bank" : Entry.Source.IsOpen ? "Ir até este Pokémon" : "Abrir o save numa aba e ir até este Pokémon";
}
