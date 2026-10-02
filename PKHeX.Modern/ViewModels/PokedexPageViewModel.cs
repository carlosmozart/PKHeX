using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Pokedex centralizada: todas as especies, com o que ha em todos os saves da pasta do Save Manager, no save
/// aberto (inclusive alteracoes nao salvas) e no bank. Filtros por situacao (possuida, faltando, shiny...),
/// geracao, tipo e fonte; resumo de living dex e shiny dex; detalhes com onde esta cada Pokemon.
/// </summary>
public sealed class PokedexPageViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private readonly Action<int, int> _goTo;
    private SaveFile? _sav;
    private IReadOnlyList<DexSource> _sources = [];
    // Saves da pasta ja lidos (caminho → data de gravacao + save), para nao reler arquivos que nao mudaram.
    private readonly Dictionary<string, (DateTime Write, SaveFile Sav)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="goTo">Ir ate um Pokemon do save aberto (caixa, slot; caixa -1 = equipe).</param>
    public PokedexPageViewModel(AppSettings settings, Action<int, int> goTo)
    {
        _settings = settings;
        _goTo = goTo;
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        SelectCommand = new RelayCommand(p => { if (p is DexCardViewModel c) Selected = c; });
        SetGenerationCommand = new RelayCommand(p => GenerationFilter = p is int g && g != GenerationFilter ? g : null);
        Generations = [.. Enumerable.Range(1, 9).Select(g => new GenerationFilterViewModel(g))];
        TypeOptions = ["Todos os tipos", .. GameInfo.Strings.types.Take(18)];
    }

    public override string Title => "Pokédex";
    public override string Icon => "📖";

    public override void Load(SaveFile sav) => _sav = sav;

    public RelayCommand RefreshCommand { get; }
    public RelayCommand SelectCommand { get; }
    public RelayCommand SetGenerationCommand { get; }

    /// <summary>Todos os cartoes (fixos, um por especie).</summary>
    public IReadOnlyList<DexCardViewModel> Cards { get; private set; } = [];
    /// <summary>Cartoes que passam nos filtros (a grade e virtualizada: so os da tela sao montados).</summary>
    public IReadOnlyList<DexCardViewModel> VisibleCards { get; private set; } = [];

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public string Summary { get; private set; } = "";
    public string Stats { get; private set; } = "";

    // Filtros
    public IReadOnlyList<string> StatusOptions { get; } =
    [
        "Todas as espécies", "Possuídas", "Faltando (living dex)", "Shiny possuídas", "Shiny faltando (shiny dex)",
        "Capturadas na Pokédex", "Vistas, não capturadas", "Nunca vistas",
    ];
    private int _status;
    public int StatusIndex { get => _status; set { if (Set(ref _status, value)) ApplyFilter(); } }

    public IReadOnlyList<string> TypeOptions { get; }
    private int _type;
    public int TypeIndex { get => _type; set { if (Set(ref _type, value)) ApplyFilter(); } }

    public IReadOnlyList<string> SourceOptions { get; private set; } = ["Tudo (saves + bank)"];
    private int _source;
    public int SourceIndex { get => _source; set { if (value >= 0 && Set(ref _source, value)) ApplyFilter(); } }
    private DexSource? Source => _source > 0 && _source <= _sources.Count ? _sources[_source - 1] : null;

    private string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value ?? "")) ApplyFilter(); } }

    public IReadOnlyList<GenerationFilterViewModel> Generations { get; }
    private int? _generation;
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

    private DexCardViewModel? _selected;
    public DexCardViewModel? Selected
    {
        get => _selected;
        set
        {
            if (_selected is not null)
                _selected.IsSelected = false;
            Set(ref _selected, value);
            if (value is not null)
                value.IsSelected = true;
            Raise(nameof(HasSelection));
            Raise(nameof(Locations));
            Raise(nameof(HasNoLocations));
        }
    }
    public bool HasSelection => Selected is not null;
    public bool HasNoLocations => Locations.Count == 0;

    /// <summary>Onde esta cada Pokemon da especie selecionada (na fonte escolhida).</summary>
    public IReadOnlyList<DexLocationViewModel> Locations => Selected is null ? []
        : [.. Selected.Entry.Owned.Where(l => Source is null || l.Source == Source)
            .OrderBy(l => !l.Source.IsOpenSave).ThenBy(l => l.Source.IsBank)
            .Select(l => new DexLocationViewModel(l, l.Source.IsOpenSave ? new RelayCommand(() => _goTo(l.Box, l.Slot)) : null))];

    /// <summary>Le tudo de novo (em segundo plano). Chamado ao abrir a pagina.</summary>
    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Summary = "Lendo saves e bank...";
        Raise(nameof(Summary));
        try
        {
            var sav = _sav;
            var openPath = sav?.Metadata.FilePath;
            var folder = _settings.SavesFolder ?? SaveLibrary.DefaultFolder;
            var (entries, sources) = await Task.Run(() => PokedexService.Build(GetSaves(sav, openPath, folder), includeBank: true));

            var keep = Source?.Id;
            var selected = Selected?.Entry.Species;
            _sources = sources;
            SourceOptions = ["Tudo (saves + bank)", .. sources.Select(s => s.Name)];
            Raise(nameof(SourceOptions));
            _source = keep is null ? 0 : Math.Max(0, sources.ToList().FindIndex(s => s.Id == keep) + 1);
            Raise(nameof(SourceIndex));

            if (Cards.Count == entries.Count)
            {
                // Mesmos cartoes, dados novos: a grade nao e recriada (1025 cartoes custam caro para montar).
                for (int i = 0; i < entries.Count; i++)
                    Cards[i].Entry = entries[i];
            }
            else
            {
                Cards = [.. entries.Select(e => new DexCardViewModel(e))];
                Raise(nameof(Cards));
            }
            Selected = selected is { } sp ? Cards.FirstOrDefault(c => c.Entry.Species == sp) : null;
            int saves = sources.Count(s => !s.IsBank);
            Summary = $"{saves} save(s) + bank · {PokedexService.MaxSpecies} espécies";
            Raise(nameof(Summary));
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Summary = $"Erro ao montar a Pokédex: {ex.Message}";
            Raise(nameof(Summary));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private IEnumerable<(string, SaveFile, bool)> GetSaves(SaveFile? open, string? openPath, string folder)
    {
        if (open is not null)
            yield return (openPath ?? "aberto", open, true);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in System.IO.Directory.Exists(folder) ? System.IO.Directory.EnumerateFiles(folder, "*", System.IO.SearchOption.AllDirectories) : [])
        {
            seen.Add(path);
            if (openPath is not null && string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(openPath), StringComparison.OrdinalIgnoreCase))
                continue;
            DateTime write;
            try { write = System.IO.File.GetLastWriteTimeUtc(path); } catch { continue; }
            if (!_cache.TryGetValue(path, out var hit) || hit.Write != write)
            {
                if (PokedexService.TryRead(path) is not { } read)
                    continue;
                _cache[path] = hit = (write, read);
            }
            yield return (path, hit.Sav, false);
        }
        foreach (var gone in _cache.Keys.Where(k => !seen.Contains(k)).ToList())
            _cache.Remove(gone);
    }

    private void ApplyFilter()
    {
        var source = Source;
        var query = _search.Trim();
        int scope = 0, owned = 0, shiny = 0, caught = 0, seen = 0;
        foreach (var c in Cards)
        {
            c.Update(source);
            var e = c.Entry;
            bool inScope = (source is null || e.Species <= source.MaxSpecies)
                           && (_generation is null || e.Generation == _generation)
                           && (_type == 0 || e.Type1 == _type - 1 || e.Type2 == _type - 1);
            if (inScope)
            {
                scope++;
                if (c.IsOwned) owned++;
                if (c.IsShinyOwned) shiny++;
                if (c.IsCaught) caught++;
                if (c.IsSeen) seen++;
            }
            bool status = _status switch
            {
                1 => c.IsOwned,
                2 => !c.IsOwned,
                3 => c.IsShinyOwned,
                4 => !c.IsShinyOwned,
                5 => c.IsCaught,
                6 => c.IsSeen && !c.IsCaught,
                7 => !c.IsSeen,
                _ => true,
            };
            bool text = query.Length == 0 || e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || (ushort.TryParse(query.TrimStart('#'), out var n) && n == e.Species);
            c.IsVisible = inScope && status && text;
        }
        string Pct(int n) => scope == 0 ? "0%" : $"{100.0 * n / scope:0.#}%";
        Stats = $"Living dex {owned}/{scope} ({Pct(owned)}) · Shiny dex {shiny}/{scope} ({Pct(shiny)}) · Capturadas na Pokédex {caught}/{scope} · Vistas {seen}/{scope}";
        Raise(nameof(Stats));
        VisibleCards = [.. Cards.Where(c => c.IsVisible)];
        Raise(nameof(VisibleCards));
        Raise(nameof(Locations));
        Raise(nameof(HasNoLocations));
        Raise(nameof(VisibleCount));
    }

    public string VisibleCount => $"{VisibleCards.Count} espécie(s) na lista";
}

/// <summary>Cartao de uma especie na Pokedex (estado conforme a fonte escolhida).</summary>
public sealed class DexCardViewModel(DexEntry entry) : ViewModelBase
{
    /// <summary>Dados da especie (trocados a cada leitura; o cartao continua o mesmo).</summary>
    public DexEntry Entry { get; set; } = entry;
    public string Number => $"#{Entry.Species:000}";
    public string Name => Entry.Name;

    private Bitmap? _sprite;
    private bool _spriteRequested;
    /// <summary>Gerado em segundo plano na primeira vez que o cartao aparece na tela (a grade fica fluida).</summary>
    public Bitmap? Sprite
    {
        get
        {
            if (_spriteRequested)
                return _sprite;
            _spriteRequested = true;
            if (SpriteService.TryGetCachedSpeciesSprite(Entry.Species, false, out var cached))
                return _sprite = cached;
            var species = Entry.Species;
            _ = Task.Run(() => SpriteService.GetSpeciesSprite(species, false)).ContinueWith(t =>
            {
                _sprite = t.Result;
                Raise(nameof(Sprite));
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return null;
        }
    }
    public Bitmap? ShinySprite => SpriteService.GetSpeciesSprite(Entry.Species, true);

    public IReadOnlyList<TypeChip> Types
    {
        get
        {
            var names = GameInfo.Strings.types;
            IEnumerable<byte> types = Entry.Type1 == Entry.Type2 ? [Entry.Type1] : [Entry.Type1, Entry.Type2];
            return [.. types.Select(t => new TypeChip(t < names.Length ? names[t] : "?", (uint)Drawing.PokeSprite.TypeColor.GetTypeSpriteColor(t).ToArgb()))];
        }
    }
    public string GenerationText => $"Geração {Entry.Generation}";

    public int OwnedCount { get; private set; }
    public bool IsOwned => OwnedCount > 0;
    public bool IsShinyOwned { get; private set; }
    public bool IsCaught { get; private set; }
    public bool IsSeen { get; private set; }
    public bool IsMissing => !IsOwned;
    public string OwnedText => OwnedCount > 1 ? $"×{OwnedCount}" : "";
    public string CaughtText { get; private set; } = "";
    public string SeenText { get; private set; } = "";
    public string StatusText => IsOwned ? $"Possuída ({OwnedCount})" : IsCaught ? "Capturada na Pokédex, mas nenhuma guardada" : IsSeen ? "Vista" : "Nunca vista";
    public string Tooltip => $"{Number} {Name} · {StatusText}{(IsShinyOwned ? " · ✨ shiny" : "")}";

    private bool _isVisible = true;
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Recalcula o estado para a fonte escolhida (null = tudo).</summary>
    public void Update(DexSource? source)
    {
        bool In(DexSource s) => source is null || s == source;
        var owned = Entry.Owned.Where(l => In(l.Source)).ToList();
        var caughtIn = Entry.CaughtIn.Where(In).ToList();
        var seenIn = Entry.SeenIn.Where(In).ToList();
        OwnedCount = owned.Count;
        IsShinyOwned = owned.Any(l => l.IsShiny);
        IsCaught = caughtIn.Count > 0 || owned.Count > 0;
        IsSeen = IsCaught || seenIn.Count > 0;
        CaughtText = caughtIn.Count == 0 ? "nenhum save" : string.Join(", ", caughtIn.Select(s => s.Name));
        SeenText = seenIn.Count == 0 ? "nenhum save" : string.Join(", ", seenIn.Select(s => s.Name));
        foreach (var p in (string[])[nameof(OwnedCount), nameof(IsOwned), nameof(IsShinyOwned), nameof(IsCaught), nameof(IsSeen), nameof(IsMissing),
                     nameof(OwnedText), nameof(CaughtText), nameof(SeenText), nameof(StatusText), nameof(Tooltip)])
            Raise(p);
    }
}

/// <summary>Um Pokemon possuido, na lista de detalhes.</summary>
public sealed class DexLocationViewModel(DexLocation location, RelayCommand? goCommand)
{
    public string Source => location.Source.Name;
    public string Where => location.Where + (location.FormName.Length > 0 ? $" · {location.FormName}" : "");
    public bool IsShiny => location.IsShiny;
    public RelayCommand? GoCommand { get; } = goCommand;
    public bool CanGo => GoCommand is not null;
}
