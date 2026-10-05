using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>Collection snapshots are read on a worker; Core context checks run in small UI chunks.</summary>
public sealed class LivingDexViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly Func<IReadOnlyList<(string Path, SaveFile Sav)>> _openSaves;
    private readonly Func<SaveFile?> _activeSave;
    private readonly Func<DbEntry, Task> _open;
    private readonly Action<ushort, GameVersion> _find;
    private readonly Dictionary<string, (DateTime Write, SaveFile Sav)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LivingDexRowViewModel> _rows = [];
    public static IReadOnlyList<GameVersion> Versions { get; } =
    [GameVersion.RD, GameVersion.GN, GameVersion.YW, GameVersion.GD, GameVersion.SI, GameVersion.C,
        GameVersion.R, GameVersion.S, GameVersion.E, GameVersion.FR, GameVersion.LG,
        GameVersion.D, GameVersion.P, GameVersion.Pt, GameVersion.HG, GameVersion.SS,
        GameVersion.B, GameVersion.W, GameVersion.B2, GameVersion.W2, GameVersion.X, GameVersion.Y,
        GameVersion.OR, GameVersion.AS, GameVersion.SN, GameVersion.MN, GameVersion.US, GameVersion.UM,
        GameVersion.GP, GameVersion.GE, GameVersion.SW, GameVersion.SH, GameVersion.BD, GameVersion.SP,
        GameVersion.PLA, GameVersion.SL, GameVersion.VL, GameVersion.ZA];

    public LivingDexViewModel(AppSettings settings, Func<IReadOnlyList<(string Path, SaveFile Sav)>> openSaves,
        Func<SaveFile?> activeSave, Func<DbEntry, Task> open, Action<ushort, GameVersion> find)
    {
        _settings = settings; _openSaves = openSaves; _activeSave = activeSave; _open = open; _find = find;
        GenerateCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsBusy);
        TargetOptions = Versions.Select(CoreAdapter.GetVersionName).ToArray();
    }

    public IReadOnlyList<string> TargetOptions { get; }
    private int _target = Array.IndexOf(Versions.ToArray(), GameVersion.SL);
    public int TargetIndex { get => _target; set { if (value >= 0 && value < Versions.Count && Set(ref _target, value)) { _targetChosen = true; Invalidate(); } } }
    private bool _targetChosen;
    private bool _forms, _shiny, _openSource = true, _folderSource = true, _bankSource = true;
    public bool IncludeForms { get => _forms; set { if (Set(ref _forms, value)) Invalidate(); } }
    public bool ShinyOnly { get => _shiny; set { if (Set(ref _shiny, value)) Invalidate(); } }
    public bool IncludeOpen { get => _openSource; set { if (Set(ref _openSource, value)) Invalidate(); } }
    public bool IncludeFolder { get => _folderSource; set { if (Set(ref _folderSource, value)) Invalidate(); } }
    public bool IncludeBank { get => _bankSource; set { if (Set(ref _bankSource, value)) Invalidate(); } }
    public RelayCommand GenerateCommand { get; }
    private bool _busy;
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); GenerateCommand.NotifyCanExecuteChanged(); } }
    private string _progress = "";
    public string Progress { get => _progress; private set => Set(ref _progress, value); }
    private string _summary = "Escolha o jogo alvo e gere o plano.";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public LivingDexPlan? Plan { get; private set; }
    public IReadOnlyList<LivingDexRowViewModel> Rows { get; private set; } = [];
    public IReadOnlyList<string> FilterOptions { get; } = ["Todas as entradas", "Faltando", "Com candidato", "Com outros exemplares"];
    private int _filter;
    public int FilterIndex { get => _filter; set { if (Set(ref _filter, value)) Filter(); } }
    private string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value ?? "")) Filter(); } }
    private LivingDexRowViewModel? _selected;
    public LivingDexRowViewModel? Selected { get => _selected; set { Set(ref _selected, value); Raise(nameof(HasSelection)); } }
    public bool HasSelection => Selected is not null;

    private int _revision;
    private void Invalidate()
    {
        _revision++; Plan = null; _rows = []; Selected = null; Filter();
        Summary = "Opções alteradas. Gere o plano novamente."; Raise(nameof(Plan));
    }
    private void Filter()
    {
        Rows = _rows.Where(r => (_filter == 0 || _filter == 1 && r.Row.Missing || _filter == 2 && !r.Row.Missing || _filter == 3 && r.Row.Others.Count > 0)
            && ($"{r.Row.Species} {r.Row.Name}".Contains(Search, StringComparison.OrdinalIgnoreCase))).ToArray();
        Raise(nameof(Rows));
    }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        // Ate o usuario escolher, o alvo acompanha o jogo do save aberto.
        if (!_targetChosen && _activeSave() is { } active && Versions.ToList().IndexOf(active.Version) is >= 0 and var index && index != _target)
        {
            _target = index;
            Raise(nameof(TargetIndex));
        }
        IsBusy = true; var revision = _revision;
        Progress = "Lendo as fontes...";
        try
        {
            var version = Versions[TargetIndex]; var forms = IncludeForms; var shiny = ShinyOnly;
            var open = IncludeOpen ? _openSaves().Select(s => (s.Path, Sav: s.Sav.Clone())).ToArray() : [];
            var folder = IncludeFolder ? _settings.SavesFolder ?? SaveLibrary.DefaultFolder : null;
            var bank = IncludeBank;
            var (entries, target) = await Task.Run(() =>
            {
                var data = PokemonDatabase.Build(open, folder, _cache, bank, readOnly: true,
                    sourceProgress: n => Dispatcher.UIThread.Post(() => Progress = $"Lendo fontes: {n}"));
                return (data, BlankSaveFile.Get(version));
            });
            var eligible = entries.Where(e => LivingDexPlanner.Eligible(e, target, shiny)).ToArray();
            var legalities = new Dictionary<DbEntry, bool>();
            for (int start = 0; start < eligible.Length; start += 8)
            {
                if (revision != _revision) return;
                // Context is always restored before yielding back to Avalonia.
                for (int i = start; i < Math.Min(start + 8, eligible.Length); i++)
                    legalities[eligible[i]] = LivingDexPlanner.AssessLegality(eligible[i], _activeSave());
                Progress = $"Conferindo candidatos: {Math.Min(start + 8, eligible.Length)} de {eligible.Length}";
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            var plan = await Task.Run(() => LivingDexPlanner.Build(entries, target, forms, shiny, legalities));
            if (revision != _revision) return;
            Plan = plan; Raise(nameof(Plan));
            _rows = plan.Rows.Select(r => new LivingDexRowViewModel(r,
                new RelayCommand(() => _find(r.Species, version)),
                r.Candidate is null ? null : new RelayCommand(() => _ = _open(r.Candidate)))).ToArray();
            Summary = forms
                ? $"{plan.Owned} de {plan.Rows.Count} entradas · {plan.Missing} faltando · {plan.Others} outros exemplares"
                : $"{plan.Owned} de {plan.Rows.Count} espécies · {plan.Missing} faltando · {plan.Others} outros exemplares";
            Selected = null; Filter(); Progress = "Plano pronto. Nenhum Pokémon foi movido.";
        }
        catch (Exception ex) { Progress = "Não foi possível planejar: " + ex.Message; }
        finally { IsBusy = false; }
    }
}

public sealed class LivingDexRowViewModel(LivingDexRow row, RelayCommand find, RelayCommand? open)
{
    public LivingDexRow Row => row;
    public string Title => $"#{row.Species:0000} {row.Name}";
    public bool Missing => row.Missing;
    public string Location => row.Candidate is null ? Loc.T("Faltando") : row.Candidate.Source.Name + " · " + row.Candidate.Where;
    public string Status => row.Candidate is null ? Loc.T("Sem candidato") : Loc.T(row.CandidateLegal ? "✓ Candidato legal" : "⚠ Candidato ilegal");
    public string BoxPlan => string.Format(Loc.T("Caixa {0} · slot {1}"), row.Box, row.Slot);
    public IReadOnlyList<string> OtherLocations => row.Others.Select(d => d.Source.Name + " · " + d.Where).ToArray();
    public bool HasOthers => row.Others.Count > 0;
    public string OthersSummary => string.Format(Loc.T("Outros exemplares: {0}"), row.Others.Count);
    public RelayCommand FindCommand => find;
    public RelayCommand? OpenCommand => open;
    public bool CanOpen => open is not null;
}
