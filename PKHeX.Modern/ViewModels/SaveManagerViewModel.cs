using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Save Manager: lista os saves da pasta configurada, agrupados por console.
/// E a tela inicial (sem save aberto) e tambem uma pagina da barra lateral.
/// </summary>
public sealed class SaveManagerViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private readonly Action<string> _open;
    private IReadOnlyList<SaveEntryViewModel> _all = [];
    private string? _currentPath;
    private readonly Func<string, string, string, Task<bool>> _confirm;
    private readonly Action<string> _status;

    /// <param name="confirm">Confirmacao (titulo, mensagem, botao) → true/false.</param>
    public SaveManagerViewModel(AppSettings settings, Action<string> open,
        Func<string, string, string, Task<bool>>? confirm = null, Action<string>? status = null)
    {
        _settings = settings;
        _open = open;
        _confirm = confirm ?? ((_, _, _) => Task.FromResult(true));
        _status = status ?? (_ => { });
        RefreshCommand = new RelayCommand(() => _ = ShowBackups ? RefreshBackupsAsync() : RefreshAsync());
        OpenFolderCommand = new RelayCommand(OpenFolder);
        OpenBackupsCommand = new RelayCommand(() => { ShowBackups = true; _ = RefreshBackupsAsync(); });
        BackToSavesCommand = new RelayCommand(() => ShowBackups = false);
        OpenBackupsFolderCommand = new RelayCommand(() => OpenInExplorer(SaveBackup.Folder));
        RestoreCommand = new RelayCommand(p => { if (p is BackupEntryViewModel b && b.Source is { } src) _ = RestoreToAsync(b, src); });
        DeleteBackupCommand = new RelayCommand(p => { if (p is BackupEntryViewModel b) _ = DeleteBackupAsync(b); });
        OpenCommand = new RelayCommand(p => { if (p is SaveEntryViewModel e) _open(e.Path); });
    }

    public override string Title => "Saves";
    public override string Icon => "🗂";

    public override void Load(SaveFile sav)
    {
        _currentPath = sav.Metadata.FilePath;
        foreach (var e in _all)
            e.IsCurrent = IsSamePath(e.Path, _currentPath);
    }

    /// <summary>Saves encontrados na pasta (a pagina Bank oferece estes no painel "Outro save").</summary>
    public IReadOnlyList<SaveEntryViewModel> Entries => _all;

    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    /// <summary>Mostra a lista de backups automaticos (feitos antes de salvar por cima de um save).</summary>
    public RelayCommand OpenBackupsCommand { get; }
    public RelayCommand BackToSavesCommand { get; }
    public RelayCommand OpenBackupsFolderCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand DeleteBackupCommand { get; }

    // Backups
    private bool _showBackups;
    /// <summary>Mostrando a lista de backups no lugar dos saves.</summary>
    public bool ShowBackups { get => _showBackups; set { if (Set(ref _showBackups, value)) Raise(nameof(ShowSaves)); } }
    public bool ShowSaves => !ShowBackups;
    public ObservableCollection<BackupGroupViewModel> BackupGroups { get; } = [];
    public string BackupFolder => SaveBackup.Folder;
    private string _backupSummary = "";
    public string BackupSummary { get => _backupSummary; private set => Set(ref _backupSummary, value); }
    public bool HasNoBackups => !IsLoading && BackupGroups.Count == 0;

    /// <summary>Le os backups (e o resumo de cada um) em segundo plano.</summary>
    public async Task RefreshBackupsAsync()
    {
        IsLoading = true;
        var items = await Task.Run(() => SaveBackup.List().Select(b => (Info: b, Entry: SaveLibrary.ReadOne(b.Path))).ToList());
        BackupGroups.Clear();
        foreach (var g in items.GroupBy(i => i.Info.SaveName, StringComparer.OrdinalIgnoreCase))
            BackupGroups.Add(new BackupGroupViewModel(g.Key, [.. g.Select(i => new BackupEntryViewModel(i.Info, i.Entry))]));
        BackupSummary = items.Count == 0 ? "" : $"{items.Count} backup(s) de {BackupGroups.Count} save(s) · até 20 por save, os mais antigos saem sozinhos";
        IsLoading = false;
        Raise(nameof(HasNoBackups));
    }

    /// <summary>Restaura o backup em <paramref name="target"/> (pergunta antes). O arquivo atual ganha um backup antes.</summary>
    public async Task RestoreToAsync(BackupEntryViewModel backup, string target)
    {
        bool isOpen = IsSamePath(target, _currentPath);
        var message = $"“{System.IO.Path.GetFileName(target)}” volta a ser como estava em {backup.When}."
                      + (File.Exists(target) ? " O arquivo atual ganha um backup antes, então dá para voltar atrás." : "")
                      + (isOpen ? " Esse save está aberto: ele será reaberto em seguida." : "");
        if (!await _confirm("Restaurar backup?", message, "Restaurar"))
            return;
        try
        {
            var safety = SaveBackup.Restore(backup.Info, target);
            _status($"Backup de {backup.When} restaurado em {target}."
                    + (safety is not null ? $" O arquivo anterior está nos backups ({System.IO.Path.GetFileName(safety)})." : ""));
        }
        catch (Exception ex)
        {
            _status($"Não deu para restaurar: {ex.Message}");
            return;
        }
        await RefreshBackupsAsync();
        if (isOpen)
            _open(target); // o MainViewModel pergunta antes de descartar alteracoes nao exportadas
    }

    private async Task DeleteBackupAsync(BackupEntryViewModel backup)
    {
        if (!await _confirm("Excluir backup?", $"O backup de {backup.SaveName} feito em {backup.When} será apagado. Isso não pode ser desfeito.", "Excluir"))
            return;
        try
        {
            SaveBackup.Delete(backup.Info);
            _status($"Backup de {backup.When} excluído.");
        }
        catch (Exception ex)
        {
            _status($"Não deu para excluir: {ex.Message}");
        }
        await RefreshBackupsAsync();
    }
    public RelayCommand OpenCommand { get; }

    public string Folder
    {
        get => _settings.SavesFolder ?? SaveLibrary.DefaultFolder;
        set
        {
            _settings.SavesFolder = value;
            _settings.Save();
            Raise();
            _ = RefreshAsync();
        }
    }

    public ObservableCollection<SaveGroupViewModel> Groups { get; } = [];

    private string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value)) ApplyFilter(); } }

    private int? _generation;
    /// <summary>Filtro por geracao (null = todas).</summary>
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
    public IReadOnlyList<GenerationFilterViewModel> Generations { get; private set; } = [];
    public RelayCommand SetGenerationCommand => new(p => GenerationFilter = p is int g && g != GenerationFilter ? g : null);

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public bool IsEmpty => !IsLoading && _all.Count == 0;
    /// <summary>"Lendo saves..." so na primeira leitura; nas releituras a lista atual continua visivel.</summary>
    public bool ShowLoading => IsLoading && _all.Count == 0;
    public bool FolderExists => Directory.Exists(Folder);

    /// <summary>Le a pasta em segundo plano e monta os cartoes na thread da interface.</summary>
    public async Task RefreshAsync()
    {
        if (IsLoading)
            return;
        IsLoading = true;
        Raise(nameof(IsEmpty));
        Raise(nameof(ShowLoading));
        var folder = Folder;
        var (entries, skipped) = await Task.Run(() => (SaveLibrary.Scan(folder, out var s), s));

        _all = [.. entries.Select(e => new SaveEntryViewModel(e)
        {
            IsCurrent = IsSamePath(e.Path, _currentPath),
        })];
        if (_generation is { } gen && !_all.Any(e => e.Entry.Generation == gen))
            _generation = null;
        Generations = [.. _all.Select(e => (int)e.Entry.Generation).Distinct().Order().Select(g => new GenerationFilterViewModel(g) { IsActive = g == _generation })];
        Summary = entries.Count == 0 ? ""
            : $"{entries.Count} save(s)" + (skipped > 0 ? $" · {skipped} arquivo(s) ignorado(s) por não serem saves" : "");
        IsLoading = false;
        foreach (var p in (string[])[nameof(IsEmpty), nameof(ShowLoading), nameof(FolderExists), nameof(Generations), nameof(GenerationFilter)])
            Raise(p);
        ApplyFilter();
    }

    private static bool IsSamePath(string a, string? b)
        => b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void ApplyFilter()
    {
        var query = _search.Trim();
        var visible = _all.Where(e =>
            (_generation is null || e.Entry.Generation == _generation) &&
            (query.Length == 0 || e.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)));
        Groups.Clear();
        foreach (var g in visible.GroupBy(e => e.Entry.Group))
            Groups.Add(new SaveGroupViewModel(g.Key, [.. g]));
    }

    private static void OpenInExplorer(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch
        {
            // sem explorador de arquivos disponivel
        }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Raise(nameof(FolderExists));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Folder) { UseShellExecute = true });
        }
        catch
        {
            // sem explorador de arquivos disponivel: a pasta ja foi criada
        }
    }
}

public sealed class GenerationFilterViewModel(int generation) : ViewModelBase
{
    public int Generation { get; } = generation;
    public string Label => $"Gen {Generation}";
    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

public sealed record SaveGroupViewModel(string Title, IReadOnlyList<SaveEntryViewModel> Items);

public sealed class SaveEntryViewModel(SaveEntry entry) : ViewModelBase
{
    public SaveEntry Entry { get; } = entry;
    public string Path => Entry.Path;
    public string FileName => System.IO.Path.GetFileName(Entry.Path);
    public string Game => Entry.Game;
    public string GenerationBadge => $"Gen {Entry.Generation}";
    /// <summary>Selo do jogo (Pokemon da capa nas cores da versao).</summary>
    public GameArt Art => GameArt.Get(Entry.Version);
    public string Trainer => Entry.Trainer + (Entry.TrainerIsFemale ? "  ♀" : "  ♂");
    public string Details => $"{Entry.Ids} · {Entry.PlayTime} · ${Entry.Money:N0}" + (Entry.Caught > 0 ? $" · {Entry.Caught} capturados" : "");
    public string LastWrite => $"Salvo em {Entry.LastWrite:dd/MM/yyyy HH:mm}";
    public string SearchText => $"{Entry.Game} {Entry.Trainer} {FileName} {Entry.Group}";

    private bool _isCurrent;
    /// <summary>Save aberto no momento (selo "Aberto").</summary>
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }

    private IReadOnlyList<Bitmap>? _party;
    /// <summary>Sprites da equipe, gerados so quando o cartao aparece.</summary>
    public IReadOnlyList<Bitmap> PartySprites => _party ??= [.. Entry.Party.Select(SpriteService.GetSprite).OfType<Bitmap>()];
}

public sealed record BackupGroupViewModel(string Title, IReadOnlyList<BackupEntryViewModel> Items);

/// <summary>Um backup na lista: resumo do save (como os cartoes de saves), data e origem.</summary>
public sealed class BackupEntryViewModel(BackupInfo info, SaveEntry? entry) : ViewModelBase
{
    public BackupInfo Info { get; } = info;
    public SaveEntry? Entry { get; } = entry;
    public string SaveName => Info.SaveName;
    public string? Source => Info.Source;
    public bool CanRestore => Source is not null;
    public string Game => Entry?.Game ?? "Arquivo não reconhecido como save";
    public string Trainer => Entry is null ? "" : Entry.Trainer + (Entry.TrainerIsFemale ? "  ♀" : "  ♂");
    public string Details => Entry is null ? "" : $"{Entry.PlayTime} · ${Entry.Money:N0}" + (Entry.Caught > 0 ? $" · {Entry.Caught} capturados" : "");
    public string When => Info.Created.ToString("dd/MM/yyyy HH:mm:ss");
    public string SourceText => Source is null ? "Origem desconhecida (backup antigo): use “Restaurar como...”" : $"Volta para: {Source}";

    private IReadOnlyList<Bitmap>? _party;
    public IReadOnlyList<Bitmap> PartySprites => _party ??= Entry is null ? [] : [.. Entry.Party.Select(SpriteService.GetSprite).OfType<Bitmap>()];
}
