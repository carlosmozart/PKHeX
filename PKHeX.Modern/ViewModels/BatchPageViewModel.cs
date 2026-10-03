using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>Onde a edicao em lote procura Pokemon no save aberto.</summary>
public enum BatchScope { CurrentBox, AllBoxes, Party, BoxesAndParty, Marked, SearchResults }

/// <summary>
/// Edicao em lote (Batch Editor): escolhe os Pokemon (caixa, save inteiro, selecionados ou resultados da Pesquisa),
/// marca acoes rapidas e/ou escreve o script do PKHeX, pre-visualiza e aplica. Aplicar entra no desfazer (Ctrl+Z).
/// </summary>
public sealed class BatchPageViewModel : PageViewModel
{
    private readonly Func<BatchScope, IReadOnlyList<BatchTarget>> _targets;
    private readonly Func<SaveFile?> _sav;
    private readonly Func<IReadOnlyList<BatchChange>, Task> _apply;

    /// <param name="targets">Pokemon do save aberto em cada escopo.</param>
    /// <param name="sav">Save aberto.</param>
    /// <param name="apply">Grava as alteracoes (pergunta, respeita o modo legal e registra o desfazer).</param>
    public BatchPageViewModel(Func<BatchScope, IReadOnlyList<BatchTarget>> targets, Func<SaveFile?> sav, Func<IReadOnlyList<BatchChange>, Task> apply)
    {
        _targets = targets;
        _sav = sav;
        _apply = apply;
        Actions = [.. BatchEditor.Actions.Select(a => new BatchActionViewModel(a, Invalidate))];
        PreviewCommand = new RelayCommand(() => _ = PreviewAsync(), () => !IsBusy);
        ApplyCommand = new RelayCommand(() => _ = ApplyAsync(), () => !IsBusy);
        AddFilterCommand = new RelayCommand(() => Insert("="));
        AddChangeCommand = new RelayCommand(() => Insert("."));
        ClearCommand = new RelayCommand(() =>
        {
            foreach (var a in Actions)
                a.IsChecked = false;
            Script = "";
        });
        PropertyNames = [.. EntityBatchEditor.Instance.Properties.SelectMany(p => p).Distinct().Order(StringComparer.OrdinalIgnoreCase)];
    }

    public override string Title => "Edição em lote";
    public override string Icon => "⚙";
    public override void Load(SaveFile sav) => Invalidate();

    public RelayCommand PreviewCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand AddFilterCommand { get; }
    public RelayCommand AddChangeCommand { get; }
    public RelayCommand ClearCommand { get; }

    public IReadOnlyList<string> ScopeOptions { get; } =
        ["Caixa atual", "Todas as caixas", "Equipe", "Caixas e equipe", "Pokémon selecionados", "Resultados da Pesquisa (deste save)"];
    private int _scope = (int)BatchScope.BoxesAndParty;
    public int ScopeIndex { get => _scope; set { if (value >= 0 && Set(ref _scope, value)) Invalidate(); } }

    private bool _includeEggs;
    public bool IncludeEggs { get => _includeEggs; set { if (Set(ref _includeEggs, value)) Invalidate(); } }

    public IReadOnlyList<BatchActionViewModel> Actions { get; }

    private string _script = "";
    /// <summary>Script do PKHeX: <c>=Prop=valor</c> filtra (<c>!</c> diferente, <c>&gt;</c> <c>&lt;</c> compara), <c>.Prop=valor</c> altera.</summary>
    public string Script { get => _script; set { if (Set(ref _script, value ?? "")) Invalidate(); } }

    public IReadOnlyList<string> PropertyNames { get; }
    private string? _property;
    public string? Property { get => _property; set => Set(ref _property, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            Set(ref _isBusy, value);
            PreviewCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    private string _error = "";
    public string Error { get => _error; private set { Set(ref _error, value); Raise(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;

    private string _summary = "Escolha os Pokémon e o que fazer, depois clique em Pré-visualizar.";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public IReadOnlyList<BatchChangeViewModel> Preview { get; private set; } = [];
    public bool HasPreview => Preview.Count > 0;

    private void Insert(string prefix)
    {
        var name = Property?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        var line = $"{prefix}{name}=";
        Script = Script.Length == 0 || Script.EndsWith('\n') ? Script + line : Script + "\n" + line;
    }

    /// <summary>Opcoes mudaram: a pre-visualizacao anterior deixa de valer.</summary>
    private void Invalidate()
    {
        Error = "";
        if (Preview.Count == 0)
            return;
        Preview = [];
        Raise(nameof(Preview));
        Raise(nameof(HasPreview));
        Summary = "As opções mudaram. Clique em Pré-visualizar de novo.";
    }

    private async Task<BatchRun?> RunAsync()
    {
        if (_sav() is not { } sav)
            return null;
        if (!BatchEditor.TryParse(Script, out var sets, out var error))
        {
            Error = error!;
            return null;
        }
        var actions = Actions.Where(a => a.IsChecked).Select(a => a.Action).ToList();
        if (actions.Count == 0 && sets.All(s => s.Instructions.Count == 0))
        {
            Error = "Marque uma ação rápida ou escreva uma alteração no script (ex.: .CurrentLevel=100).";
            return null;
        }
        Error = "";
        var targets = _targets((BatchScope)_scope);
        if (targets.Count == 0)
        {
            Error = (BatchScope)_scope switch
            {
                BatchScope.Marked => "Nenhum Pokémon selecionado neste save. Selecione slots nas caixas (Ctrl+clique) e volte aqui.",
                BatchScope.SearchResults => "A Pesquisa não tem resultados deste save. Faça uma pesquisa e volte aqui.",
                _ => "Não há Pokémon nesse grupo.",
            };
            return null;
        }
        IsBusy = true;
        Summary = $"Analisando {targets.Count} Pokémon...";
        try
        {
            var eggs = _includeEggs;
            return await Task.Run(() => BatchEditor.Run(sav, targets, sets, actions, eggs));
        }
        catch (Exception ex)
        {
            Error = $"Erro na edição em lote: {ex.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PreviewAsync()
    {
        if (await RunAsync() is not { } run)
        {
            Summary = "";
            return;
        }
        Preview = [.. run.Changes.Select(c => new BatchChangeViewModel(c))];
        Raise(nameof(Preview));
        Raise(nameof(HasPreview));
        Summary = Describe(run) + (run.Changes.Count > 0 ? " Nada foi gravado ainda: clique em Aplicar." : "");
    }

    private async Task ApplyAsync()
    {
        if (await RunAsync() is not { } run)
        {
            Summary = "";
            return;
        }
        if (run.Changes.Count == 0)
        {
            Summary = Describe(run);
            return;
        }
        await _apply(run.Changes);
        Preview = [];
        Raise(nameof(Preview));
        Raise(nameof(HasPreview));
        Summary = Describe(run).Replace("seriam alterados", "processados") + " Use Ctrl+Z para desfazer.";
    }

    private static string Describe(BatchRun run)
    {
        int illegal = run.Changes.Count(c => c.BecomesIllegal);
        var text = run.Matched == run.Checked
            ? $"{run.Changes.Count} de {run.Checked} Pokémon seriam alterados."
            : $"{run.Matched} de {run.Checked} Pokémon passam nos filtros; {run.Changes.Count} seriam alterados.";
        if (illegal > 0)
            text += $" {illegal} ficariam ilegais (⚠).";
        if (run.Errors > 0)
            text += $" {run.Errors} instrução(ões) não puderam ser aplicadas (propriedade que não existe nesta geração ou valor inválido).";
        return text;
    }
}

public sealed class BatchActionViewModel(BatchAction action, Action changed) : ViewModelBase
{
    public BatchAction Action { get; } = action;
    public string Name => Action.Name;
    public string Tip => Action.Tip;
    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set { if (Set(ref _isChecked, value)) changed(); } }
}

/// <summary>Uma linha da pre-visualizacao: antes → depois.</summary>
public sealed class BatchChangeViewModel(BatchChange change)
{
    public BatchChange Change { get; } = change;
    private Bitmap? _sprite;
    private bool _loaded;
    public Bitmap? Sprite
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                try { _sprite = SpriteService.GetSprite(Change.Result); } catch { _sprite = null; }
            }
            return _sprite;
        }
    }

    public string Name => Change.Result.IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[Change.Result.Species];
    public string Where => Change.Target.Where;
    public bool IsShiny => Change.Result.IsShiny;
    public bool BecomesIllegal => Change.BecomesIllegal;
    public bool IsLegal => Change.IsLegal == true;
    public string Diff => string.Join(" · ", BatchDiff.Describe(Change.Target.Pkm, Change.Result).DefaultIfEmpty("outros campos"));
}

/// <summary>Resume o que mudou num Pokemon, nos campos que a interface mostra.</summary>
public static class BatchDiff
{
    public static IEnumerable<string> Describe(PKM a, PKM b)
    {
        if (a.Species != b.Species)
            yield return $"{CoreAdapter.SpeciesNames[a.Species]} → {CoreAdapter.SpeciesNames[b.Species]}";
        if (a.CurrentLevel != b.CurrentLevel)
            yield return $"Nv. {a.CurrentLevel} → {b.CurrentLevel}";
        if (a.IsShiny != b.IsShiny)
            yield return b.IsShiny ? "shiny" : "sem shiny";
        if (IvTotal(a) != IvTotal(b))
            yield return $"IVs {IvTotal(a)} → {IvTotal(b)}";
        if (EvTotal(a) != EvTotal(b))
            yield return $"EVs {EvTotal(a)} → {EvTotal(b)}";
        if (a.Nature != b.Nature && b.Format >= 3)
            yield return $"natureza {Name(CoreAdapter.NatureNames, (int)b.Nature)}";
        if (a.HeldItem != b.HeldItem)
            yield return b.HeldItem == 0 ? "sem item" : $"@ {CoreAdapter.GetHeldItemName(b)}";
        if (a.Ball != b.Ball)
            yield return $"bola {Name(GameInfo.Strings.balllist, b.Ball)}";
        if (a.Move1 != b.Move1 || a.Move2 != b.Move2 || a.Move3 != b.Move3 || a.Move4 != b.Move4)
            yield return "golpes: " + string.Join(", ", new[] { b.Move1, b.Move2, b.Move3, b.Move4 }.Where(m => m != 0).Select(m => Name(CoreAdapter.MoveNames, m)));
        if (a.CurrentFriendship != b.CurrentFriendship && !b.IsEgg)
            yield return $"felicidade {a.CurrentFriendship} → {b.CurrentFriendship}";
        if (a.Nickname != b.Nickname)
            yield return $"apelido “{b.Nickname}”";
        if (a.OriginalTrainerName != b.OriginalTrainerName)
            yield return $"treinador {b.OriginalTrainerName}";
        if (a.Status_Condition != b.Status_Condition || a.Move1_PP != b.Move1_PP || a.Move2_PP != b.Move2_PP || a.Move3_PP != b.Move3_PP || a.Move4_PP != b.Move4_PP)
            yield return "curado";
    }

    private static int IvTotal(PKM pk) => pk.IV_HP + pk.IV_ATK + pk.IV_DEF + pk.IV_SPA + pk.IV_SPD + pk.IV_SPE;
    private static int EvTotal(PKM pk) => pk.EV_HP + pk.EV_ATK + pk.EV_DEF + pk.EV_SPA + pk.EV_SPD + pk.EV_SPE;
    private static string Name(IReadOnlyList<string> list, int i) => (uint)i < (uint)list.Count ? list[i] : "?";
}
