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

    private readonly Func<string, string, string, IReadOnlyList<string>?, Task<bool>> _confirm;
    private readonly Action<string> _report;

    /// <param name="goTo">Ir ate um Pokemon do save aberto (caixa, slot; caixa -1 = equipe).</param>
    /// <param name="confirm">Confirmacao (titulo, mensagem, botao, detalhes) → true/false.</param>
    public PokedexPageViewModel(AppSettings settings, Action<int, int> goTo,
        Func<string, string, string, IReadOnlyList<string>?, Task<bool>>? confirm = null, Action<string>? status = null)
    {
        _settings = settings;
        _goTo = goTo;
        _confirm = confirm ?? ((_, _, _, _) => Task.FromResult(true));
        _report = status ?? (_ => { });
        SyncCommand = new RelayCommand(() => _ = SyncAsync(), () => _sav is { HasPokeDex: true } && !IsBusy);
        SyncAllCommand = new RelayCommand(() => _ = SyncAllAsync(), () => !IsBusy && _sources.Count(x => !x.IsBank) > 1);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        SelectCommand = new RelayCommand(p => { if (p is DexCardViewModel c) Selected = c; });
        SetGenerationCommand = new RelayCommand(p => GenerationFilter = p is int g && g != GenerationFilter ? g : null);
        Generations = [.. Enumerable.Range(1, 9).Select(g => new GenerationFilterViewModel(g))];
        TypeOptions = ["Todos os tipos", .. GameInfo.Strings.types.Take(18)];
    }

    public override string Title => "Pokédex";
    public override string Icon => "📖";

    public override void Load(SaveFile sav)
    {
        _sav = sav;
        SyncCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Sincronizar: registra na Pokedex do save aberto o que foi capturado/visto nos outros saves e o que voce tem guardado.</summary>
    public RelayCommand SyncCommand { get; }
    /// <summary>Sincronizar todos: o mesmo para cada save da pasta (os fechados sao gravados na hora, com backup).</summary>
    public RelayCommand SyncAllCommand { get; }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand SelectCommand { get; }
    public RelayCommand SetGenerationCommand { get; }

    private IReadOnlyList<DexCardViewModel> _speciesCards = [];
    private IReadOnlyList<DexCardViewModel> _formCards = [];
    private IReadOnlyList<DexEntry> _species = [];
    /// <summary>Todos os cartoes da lista atual (especies ou formas/generos).</summary>
    public IReadOnlyList<DexCardViewModel> Cards => ShowForms ? _formCards : _speciesCards;

    private bool _showForms;
    /// <summary>Lista com cada forma e genero como entrada propria (Vulpix de Alola, Unown A–?, Pyroar ♀...).</summary>
    public bool ShowForms
    {
        get => _showForms;
        set
        {
            if (!Set(ref _showForms, value))
                return;
            var species = Selected?.Entry.Species;
            Raise(nameof(Cards));
            Selected = species is { } sp ? Cards.FirstOrDefault(c => c.Entry.Species == sp) : null;
            ApplyFilter();
        }
    }
    /// <summary>Cartoes que passam nos filtros (a grade e virtualizada: so os da tela sao montados).</summary>
    public IReadOnlyList<DexCardViewModel> VisibleCards { get; private set; } = [];

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); SyncCommand.NotifyCanExecuteChanged(); SyncAllCommand.NotifyCanExecuteChanged(); } }
    public string Summary { get; private set; } = "";
    public string Stats { get; private set; } = "";

    // Filtros
    public IReadOnlyList<string> StatusOptions { get; } =
    [
        "Todas as espécies", "Possuídas", "Faltando (living dex)", "Shiny possuídas", "Shiny faltando (shiny dex)",
        "Capturadas na Pokédex", "Vistas, não capturadas", "Nunca vistas", "Alpha possuídas",
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
            var data = await Task.Run(() => PokedexService.Build(GetSaves(sav, openPath, folder), includeBank: true));
            var sources = data.Sources;
            _species = data.Species;

            var keep = Source?.Id;
            var selected = Selected?.Entry.Species;
            _sources = sources;
            SourceOptions = ["Tudo (saves + bank)", .. sources.Select(s => s.Name)];
            Raise(nameof(SourceOptions));
            _source = keep is null ? 0 : Math.Max(0, sources.ToList().FindIndex(s => s.Id == keep) + 1);
            Raise(nameof(SourceIndex));

            _speciesCards = Reuse(_speciesCards, data.Species);
            _formCards = Reuse(_formCards, data.Forms);
            Raise(nameof(Cards));
            Selected = selected is { } sp ? Cards.FirstOrDefault(c => c.Entry.Species == sp) : null;
            int saves = sources.Count(s => !s.IsBank);
            Summary = $"{saves} save(s) + bank · {PokedexService.MaxSpecies} espécies · {data.Forms.Count} formas e gêneros";
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

    /// <summary>Mesmos cartoes com dados novos (a grade nao e recriada); cria se a quantidade mudou.</summary>
    private static IReadOnlyList<DexCardViewModel> Reuse(IReadOnlyList<DexCardViewModel> cards, IReadOnlyList<DexEntry> entries)
    {
        if (cards.Count != entries.Count)
            return [.. entries.Select(e => new DexCardViewModel(e))];
        for (int i = 0; i < entries.Count; i++)
            cards[i].Entry = entries[i];
        return cards;
    }

    /// <summary>
    /// Sincroniza a Pokedex do save aberto: especies capturadas em outros saves ou guardadas em qualquer lugar (caixas,
    /// equipe, bank) passam a capturadas; as so vistas em outros saves passam a vistas (quando o jogo permite).
    /// </summary>
    private async Task SyncAsync()
    {
        if (_sav is not { HasPokeDex: true } sav || _species.Count == 0)
            return;
        var max = Math.Min(sav.MaxSpeciesID, PokedexService.MaxSpecies);
        bool Caught(ushort s) { try { return sav.GetCaught(s); } catch { return true; } }
        bool Seen(ushort s) { try { return sav.GetSeen(s); } catch { return true; } }
        var toCaught = _species.Where(e => e.Species <= max && !Caught(e.Species)
            && (e.CaughtIn.Any(x => !x.IsOpenSave) || e.Owned.Count > 0)).ToList();
        var toSeen = _species.Where(e => e.Species <= max && !Seen(e.Species) && !toCaught.Contains(e)
            && e.SeenIn.Any(x => !x.IsOpenSave)).ToList();
        if (toSeen.Count > 0 && !PokedexService.CanRegisterSeen(sav, toSeen[0].Species))
            toSeen.Clear(); // jogo que nao aceita "so vista" (Gen 7)
        var game = CoreAdapter.GetGameName(sav);
        if (toCaught.Count == 0 && toSeen.Count == 0)
        {
            _report($"A Pokédex de {game} já tem tudo o que os outros saves e o bank têm.");
            return;
        }
        var details = toCaught.Take(25).Select(e => $"✓ #{e.Species:000} {e.Name} → capturada")
            .Concat(toSeen.Take(10).Select(e => $"👁 #{e.Species:000} {e.Name} → vista")).ToList();
        if (toCaught.Count > 25 || toSeen.Count > 10)
            details.Add($"… {toCaught.Count + toSeen.Count} no total.");
        if (!await _confirm("Sincronizar a Pokédex?",
                $"Na Pokédex de {game}: {toCaught.Count} espécie(s) passam a capturadas (capturadas em outro save ou guardadas nas caixas/bank) e {toSeen.Count} a vistas. "
                + "Não entra no Ctrl+Z; o arquivo só muda ao salvar (e o anterior vai para os backups).",
                "Sincronizar", details))
            return;
        int caught = 0, seen = 0, skipped = 0;
        foreach (var e in toCaught)
        {
            if (PokedexService.RegisterCaught(sav, e.Species)) caught++;
            else skipped++;
        }
        foreach (var e in toSeen)
        {
            if (PokedexService.RegisterSeen(sav, e.Species)) seen++;
            else skipped++;
        }
        if (caught + seen > 0)
            Changed?.Invoke();
        _report($"Pokédex de {game}: {caught} capturada(s) e {seen} vista(s) registradas."
                + (skipped > 0 ? $" {skipped} ficaram de fora (não existem na Pokédex deste jogo ou o jogo não permite marcar só como vista)." : "")
                + " Salve o save para gravar.");
        await RefreshAsync();
    }

    /// <summary>Uma linha do plano de "Sincronizar todos": o save, o que muda e se e o save aberto.</summary>
    private sealed record SyncPlan(DexSource Source, SaveFile Sav, string Path, List<ushort> ToCaught, List<ushort> ToSeen);

    /// <summary>
    /// Sincroniza a Pokedex de todos os saves: o que foi capturado em qualquer save ou esta guardado em qualquer lugar
    /// (caixas, equipe, bank) passa a capturado em cada um; o que so foi visto passa a visto. Os saves fechados sao
    /// gravados na hora (o arquivo anterior vai para os backups); o save aberto muda na memoria e precisa ser salvo.
    /// </summary>
    private async Task SyncAllAsync()
    {
        if (_species.Count == 0)
            return;
        IsBusy = true;
        List<SyncPlan> plans;
        try
        {
            var species = _species;
            var sources = _sources.Where(x => !x.IsBank).ToList();
            var openSav = _sav;
            plans = await Task.Run(() =>
            {
                var result = new List<SyncPlan>();
                foreach (var src in sources)
                {
                    var sav = src.IsOpenSave ? openSav : PokedexService.TryRead(src.Id);
                    if (sav is not { HasPokeDex: true })
                        continue;
                    var max = Math.Min(sav.MaxSpeciesID, PokedexService.MaxSpecies);
                    bool Caught(ushort sp) { try { return sav.GetCaught(sp); } catch { return true; } }
                    bool Seen(ushort sp) { try { return sav.GetSeen(sp); } catch { return true; } }
                    var toCaught = species.Where(e => e.Species <= max && !Caught(e.Species)
                        && (e.CaughtIn.Any(x => x.Id != src.Id) || e.Owned.Count > 0)).Select(e => e.Species).ToList();
                    var caughtSet = toCaught.ToHashSet();
                    var toSeen = species.Where(e => e.Species <= max && !caughtSet.Contains(e.Species) && !Seen(e.Species)
                        && e.SeenIn.Any(x => x.Id != src.Id)).Select(e => e.Species).ToList();
                    if (toSeen.Count > 0 && !PokedexService.CanRegisterSeen(sav, toSeen[0]))
                        toSeen.Clear(); // jogo que nao aceita "so vista" (Gen 7): nao promete o que nao vai entrar
                    if (toCaught.Count + toSeen.Count > 0)
                        result.Add(new SyncPlan(src, sav, src.Id, toCaught, toSeen));
                }
                return result;
            });
        }
        finally
        {
            IsBusy = false;
        }
        if (plans.Count == 0)
        {
            _report("Todas as Pokédex já têm tudo o que os outros saves e o bank têm.");
            return;
        }
        var details = plans.Select(x => $"{x.Source.Name}: {x.ToCaught.Count} capturada(s), {x.ToSeen.Count} vista(s)"
                                         + (x.Source.IsOpenSave ? " — salve depois" : "")).ToList();
        if (!await _confirm("Sincronizar todas as Pokédex?",
                $"{plans.Count} save(s) recebem as espécies capturadas ou vistas nos outros saves e as guardadas nas caixas/bank. "
                + "Os saves fechados são gravados agora (cada arquivo anterior vai para Saves › Backups); o save aberto muda na memória e precisa ser salvo.",
                "Sincronizar todos", details))
            return;

        IsBusy = true;
        _report("Sincronizando as Pokédex...");
        var results = new List<string>();
        int openChanged = 0;
        try
        {
            foreach (var plan in plans)
            {
                var (caught, seen, error) = await Task.Run(() =>
                {
                    int c = 0, v = 0;
                    foreach (var sp in plan.ToCaught)
                        if (PokedexService.RegisterCaught(plan.Sav, sp)) c++;
                    foreach (var sp in plan.ToSeen)
                        if (PokedexService.RegisterSeen(plan.Sav, sp)) v++;
                    if (plan.Source.IsOpenSave || c + v == 0)
                        return (c, v, (string?)null);
                    try
                    {
                        SaveBackup.BeforeOverwrite(ZipSaves.FileOf(plan.Path));
                        CoreAdapter.ExportSave(plan.Sav, plan.Path);
                        return (c, v, (string?)null);
                    }
                    catch (Exception ex)
                    {
                        return (c, v, ex.Message);
                    }
                });
                if (plan.Source.IsOpenSave)
                    openChanged = caught + seen;
                results.Add(error is null
                    ? $"{plan.Source.Name}: {caught} capturada(s), {seen} vista(s)"
                    : $"{plan.Source.Name}: não gravado ({error})");
            }
        }
        finally
        {
            IsBusy = false;
        }
        if (openChanged > 0)
            Changed?.Invoke();
        _report("Pokédex sincronizadas. " + string.Join(" · ", results) + (openChanged > 0 ? " Salve o save aberto para gravar." : ""));
        await RefreshAsync();
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
        int scope = 0, owned = 0, shiny = 0, caught = 0, seen = 0, alpha = 0;
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
                if (c.IsAlphaOwned) alpha++;
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
                8 => c.IsAlphaOwned,
                _ => true,
            };
            bool text = query.Length == 0 || e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || e.FormName.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || (ushort.TryParse(query.TrimStart('#'), out var n) && n == e.Species);
            c.IsVisible = inScope && status && text;
        }
        string Pct(int n) => scope == 0 ? "0%" : $"{100.0 * n / scope:0.#}%";
        var what = ShowForms ? "formas" : "espécies";
        Stats = $"Living dex {owned}/{scope} {what} ({Pct(owned)}) · Shiny dex {shiny}/{scope} ({Pct(shiny)}) · Capturadas na Pokédex {caught}/{scope} · Vistas {seen}/{scope}"
                + (alpha > 0 ? $" · Alpha {alpha}" : "");
        Raise(nameof(Stats));
        VisibleCards = [.. Cards.Where(c => c.IsVisible)];
        Raise(nameof(VisibleCards));
        Raise(nameof(Locations));
        Raise(nameof(HasNoLocations));
        Raise(nameof(VisibleCount));
    }

    public string VisibleCount => $"{VisibleCards.Count} {(ShowForms ? "forma(s)" : "espécie(s)")} na lista";
}

/// <summary>Cartao de uma especie na Pokedex (estado conforme a fonte escolhida).</summary>
public sealed class DexCardViewModel(DexEntry entry) : ViewModelBase
{
    private DexEntry _entry = entry;
    /// <summary>Dados da especie (trocados a cada leitura; o cartao continua o mesmo).</summary>
    public DexEntry Entry
    {
        get => _entry;
        set
        {
            if (value.Species != _entry.Species || value.Form != _entry.Form || value.Gender != _entry.Gender)
            {
                _spriteRequested = false;
                _sprite = null;
                Raise(nameof(Sprite));
            }
            _entry = value;
        }
    }
    public string Number => $"#{Entry.Species:000}";
    public string Name => Entry.Name;
    /// <summary>Nome da forma/genero (vazio na lista por especie ou na forma unica).</summary>
    public string FormName => Entry.FormName;
    public bool HasFormName => Entry.FormName.Length > 0;

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
            var (species, form, gender, context) = (Entry.Species, Entry.Form, Math.Max(0, (int)Entry.Gender), Entry.Context);
            if (SpriteService.TryGetCachedSpeciesSprite(species, false, out var cached, form, gender))
                return _sprite = cached;
            _ = Task.Run(() => SpriteService.GetSpeciesSprite(species, false, form, gender, context)).ContinueWith(t =>
            {
                _sprite = t.Result;
                Raise(nameof(Sprite));
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return null;
        }
    }
    public Bitmap? ShinySprite => SpriteService.GetSpeciesSprite(Entry.Species, true, Entry.Form, Math.Max(0, (int)Entry.Gender), Entry.Context);

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
    /// <summary>Tem um Alpha (Legends) desta especie/forma.</summary>
    public bool IsAlphaOwned { get; private set; }
    public bool IsCaught { get; private set; }
    public bool IsSeen { get; private set; }
    public bool IsMissing => !IsOwned;
    public string OwnedText => OwnedCount > 1 ? $"×{OwnedCount}" : "";
    public string CaughtText { get; private set; } = "";
    public string SeenText { get; private set; } = "";
    public string StatusText => IsOwned ? $"Possuída ({OwnedCount})" : IsCaught ? "Capturada na Pokédex, mas nenhuma guardada" : IsSeen ? "Vista" : "Nunca vista";
    public string Tooltip => $"{Number} {Name}{(HasFormName ? $" ({FormName})" : "")} · {StatusText}{(IsShinyOwned ? " · ✨ shiny" : "")}{(IsAlphaOwned ? " · alpha" : "")}";

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
        IsAlphaOwned = owned.Any(l => l.IsAlpha);
        IsCaught = caughtIn.Count > 0 || owned.Count > 0;
        IsSeen = IsCaught || seenIn.Count > 0;
        CaughtText = caughtIn.Count == 0 ? "nenhum save" : string.Join(", ", caughtIn.Select(s => s.Name));
        SeenText = seenIn.Count == 0 ? "nenhum save" : string.Join(", ", seenIn.Select(s => s.Name));
        foreach (var p in (string[])[nameof(OwnedCount), nameof(IsOwned), nameof(IsShinyOwned), nameof(IsAlphaOwned), nameof(FormName), nameof(HasFormName), nameof(Number), nameof(Name), nameof(IsCaught), nameof(IsSeen), nameof(IsMissing),
                     nameof(OwnedText), nameof(CaughtText), nameof(SeenText), nameof(StatusText), nameof(Tooltip)])
            Raise(p);
    }
}

/// <summary>Um Pokemon possuido, na lista de detalhes.</summary>
public sealed class DexLocationViewModel(DexLocation location, RelayCommand? goCommand)
{
    public string Source => location.Source.Name;
    public string Where => location.Where + (location.FormName.Length > 0 ? $" · {location.FormName}" : "") + (location.IsAlpha ? " · alpha" : "");
    public bool IsShiny => location.IsShiny;
    public RelayCommand? GoCommand { get; } = goCommand;
    public bool CanGo => GoCommand is not null;
}
