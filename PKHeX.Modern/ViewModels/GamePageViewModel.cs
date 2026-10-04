using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Editores por jogo: flags de evento, valores de evento e recordes do save aberto. As alteracoes valem na hora
/// (como no Treinador) e marcam o save como alterado; exporte o save para gravar no arquivo.
/// </summary>
public sealed partial class GamePageViewModel : PageViewModel
{
    private readonly Func<string, string, string, Task<bool>> _confirm;
    private readonly Action<string> _status;
    private GameEditors? _editors;
    private List<FlagRowViewModel> _flags = [];
    private List<WorkRowViewModel> _works = [];
    private List<RecordRowViewModel> _records = [];
    private List<ShortcutRowViewModel> _shortcuts = [];

    /// <param name="confirm">Pergunta (titulo, mensagem, botao) antes de mudar varias flags.</param>
    /// <summary>Antes de um atalho: grava o que a mochila tem pendente na tela.</summary>
    public Action? BeforeShortcut { get; set; }
    /// <summary>Depois de um atalho: rele as paginas que o atalho pode ter mudado (mochila, Pokedex).</summary>
    public Action? AfterShortcut { get; set; }

    public GamePageViewModel(Func<string, string, string, Task<bool>> confirm, Action<string> status)
    {
        _confirm = confirm;
        _status = status;
        SetTabCommand = new RelayCommand(p => { if (p is string s && int.TryParse(s, out var t)) Tab = t; });
        SetVisibleCommand = new RelayCommand(p => _ = SetVisibleAsync(p is "1"));
        RegisterFameCommand = new RelayCommand(RegisterFame, () => _sav is not null && HasFame);
        ApplyFameCommand = new RelayCommand(ApplyFameMember, () => SelectedFameMember is not null);
    }

    private SaveFile? _sav;

    public override string Title => "Jogo";
    public override string Icon => "🎮";
    public override bool IsAvailable => _editors is null || _editors.IsAvailable;

    public override void Load(SaveFile sav)
    {
        _editors = new GameEditors(sav);
        _sav = sav;
        RefreshCards();
        FameCaps = HallOfFame.Caps(sav);
        SpeciesOptions = [.. Enumerable.Range(1, FameCaps.MaxSpecies).Select(i => GameInfo.Strings.Species[i])];
        SelectedFameMember = null;
        var e = _editors;
        void Changed() => this.Changed?.Invoke();
        _flags = [.. e.Flags.Select(f => new FlagRowViewModel(f, e, Changed))];
        _works = [.. e.Works.Select(w => new WorkRowViewModel(w, e, Changed))];
        _records = [.. e.Records.Select(r => new RecordRowViewModel(r, e, Changed))];
        _shortcuts = [.. e.Shortcuts.Select(s => new ShortcutRowViewModel(s, () => _ = RunShortcutAsync(s)))];
        ShortcutRows = _shortcuts;
        FameRows = BuildFame(e.Fame);
        Categories = ["Todas as categorias", .. e.Flags.Select(GameEditors.CategoryOf).Concat(e.Works.Select(GameEditors.CategoryOf))
            .Where(c => c.Length > 0).Distinct().Order(StringComparer.CurrentCulture)];
        _category = 0;
        _showUnnamed = !e.HasLabels;
        _tab = e.HasEvents ? 0 : e.WorkCount > 0 ? 1 : e.HasRecords ? 2 : e.HasShortcuts ? 3 : 4;
        Raise(string.Empty);
        ApplyFilter();
    }

    public RelayCommand SetTabCommand { get; }
    public RelayCommand SetVisibleCommand { get; }

    public bool HasEvents => _editors?.HasEvents == true;
    public bool HasWorks => _editors?.WorkCount > 0;
    public bool HasRecords => _editors?.HasRecords == true;
    public bool HasShortcuts => _editors?.HasShortcuts == true;
    public IReadOnlyList<ShortcutRowViewModel> ShortcutRows { get; private set; } = [];
    public bool HasFame => _editors?.HasFame == true;
    public IReadOnlyList<FameTeamViewModel> FameRows { get; private set; } = [];
    public string Summary => _editors is null ? "" : string.Join(" · ", new[]
    {
        HasEvents ? $"{_editors.FlagCount} flags ({_flags.Count(f => f.HasName)} com nome)" : "",
        HasWorks ? $"{_editors.WorkCount} valores ({_works.Count(w => w.HasName)} com nome)" : "",
        HasRecords ? $"{_records.Count} recordes" : "",
        HasShortcuts ? $"{_shortcuts.Count} atalhos" : "",
        HasFame ? $"{FameRows.Count} {(FameRows.Count == 1 ? "equipe" : "equipes")} no Hall da Fama" : "",
    }.Where(s => s.Length > 0));
    public bool NoLabels => HasEvents && _editors?.HasLabels == false;

    private int _tab;
    /// <summary>0 = flags, 1 = valores, 2 = recordes, 3 = atalhos, 4 = Hall da Fama.</summary>
    public int Tab
    {
        get => _tab;
        set
        {
            if (!Set(ref _tab, value))
                return;
            Raise(nameof(IsFlagsTab));
            Raise(nameof(IsWorksTab));
            Raise(nameof(IsRecordsTab));
            Raise(nameof(IsShortcutsTab));
            Raise(nameof(IsFameTab));
            Raise(nameof(IsCardsTab));
            Raise(nameof(ShowFilters));
            Raise(nameof(ShowCategory));
            ApplyFilter();
        }
    }
    public bool IsFlagsTab => _tab == 0;
    public bool IsWorksTab => _tab == 1;
    public bool IsRecordsTab => _tab == 2;
    public bool IsShortcutsTab => _tab == 3;
    public bool IsFameTab => _tab == 4;
    public bool ShowCategory => _tab is 0 or 1;
    /// <summary>Busca e filtros (atalhos e Hall da Fama nao tem).</summary>
    public bool ShowFilters => _tab < 3;

    // Filtros
    private string _query = "";
    /// <summary>Texto no nome ou numero (ex.: "surf", "#120").</summary>
    public string Query { get => _query; set { if (Set(ref _query, value ?? "")) ApplyFilter(); } }

    public IReadOnlyList<string> Categories { get; private set; } = ["Todas as categorias"];
    private int _category;
    public int CategoryIndex { get => _category; set { if (value >= 0 && Set(ref _category, value)) ApplyFilter(); } }

    private bool _showUnnamed;
    public bool ShowUnnamed { get => _showUnnamed; set { if (Set(ref _showUnnamed, value)) ApplyFilter(); } }

    private bool _onlySet;
    /// <summary>So flags ativadas / valores diferentes de zero / recordes acima de zero.</summary>
    public bool OnlySet { get => _onlySet; set { if (Set(ref _onlySet, value)) ApplyFilter(); } }

    public IReadOnlyList<FlagRowViewModel> FlagRows { get; private set; } = [];
    public IReadOnlyList<WorkRowViewModel> WorkRows { get; private set; } = [];
    public IReadOnlyList<RecordRowViewModel> RecordRows { get; private set; } = [];
    public string CountText { get; private set; } = "";
    public bool HasNoRows { get; private set; }

    private bool Matches(int index, bool hasName, string name, string category, string code = "")
    {
        if (!_showUnnamed && !hasName)
            return false;
        if (_category > 0 && _tab != 2 && category != Categories[_category])
            return false;
        var q = _query.Trim();
        if (q.Length == 0)
            return true;
        if (q.StartsWith('#') && int.TryParse(q[1..], out var n))
            return index == n;
        return name.Contains(q, StringComparison.OrdinalIgnoreCase) || index.ToString() == q
            || (code.Length > 0 && code.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyFilter()
    {
        int shown, total;
        switch (_tab)
        {
            case 0:
                FlagRows = [.. _flags.Where(f => Matches(f.Index, f.HasName, f.Name, f.Category, f.Code) && (!_onlySet || f.IsSet))];
                (shown, total) = (FlagRows.Count, _flags.Count);
                break;
            case 1:
                WorkRows = [.. _works.Where(w => Matches(w.Index, w.HasName, w.Name, w.Category, w.Code) && (!_onlySet || w.Value != 0))];
                (shown, total) = (WorkRows.Count, _works.Count);
                break;
            case 2:
                RecordRows = [.. _records.Where(r => Matches(r.Id, r.HasName, r.Name, "") && (!_onlySet || r.Value != 0))];
                (shown, total) = (RecordRows.Count, _records.Count);
                break;
            case 3:
                (shown, total) = (_shortcuts.Count, _shortcuts.Count);
                break;
            default:
                (shown, total) = (FameRows.Count, FameRows.Count);
                break;
        }
        CountText = shown == total ? $"{total} itens" : $"{shown} de {total} itens";
        HasNoRows = shown == 0 && _tab < 4; // As abas especiais tem seus proprios avisos.
        foreach (var p in (string[])[nameof(FlagRows), nameof(WorkRows), nameof(RecordRows), nameof(CountText), nameof(HasNoRows)])
            Raise(p);
    }

    // Hall da Fama: as alteracoes valem na hora no save (como as flags) e marcam o save como alterado.
    public FameCaps FameCaps { get; private set; } = new(0, false, false, 0);
    public bool FameHasNickname => FameCaps.NicknameLength > 0;
    public bool FameHasShiny => FameCaps.HasShiny;
    public bool FameHasDate => FameCaps.HasDate;
    public IReadOnlyList<string> SpeciesOptions { get; private set; } = [];
    public bool HasFameTeams => FameRows.Count > 0;
    public RelayCommand RegisterFameCommand { get; }
    public RelayCommand ApplyFameCommand { get; }

    private FameMemberViewModel? _selectedFame;
    /// <summary>Pokemon do Hall da Fama em edicao (clique no Pokemon da equipe).</summary>
    public FameMemberViewModel? SelectedFameMember
    {
        get => _selectedFame;
        set
        {
            if (_selectedFame is not null) _selectedFame.IsSelected = false;
            Set(ref _selectedFame, value);
            if (value is not null)
            {
                value.IsSelected = true;
                FameSpecies = GameInfo.Strings.Species[value.Member.Species];
                FameNickname = value.Member.Nickname;
                FameLevel = value.Member.Level > 0 ? value.Member.Level : 1;
                FameShiny = value.Member.Shiny;
            }
            foreach (var p in (string[])[nameof(HasFameSelection), nameof(FameSpecies), nameof(FameNickname), nameof(FameLevel), nameof(FameShiny), nameof(FameSelectionTitle)])
                Raise(p);
            ApplyFameCommand.NotifyCanExecuteChanged();
        }
    }
    public bool HasFameSelection => _selectedFame is not null;
    public string FameSelectionTitle => _selectedFame is { } m ? $"{m.TeamTitle} · posição {m.Member.Slot + 1}" : "";
    public string FameSpecies { get; set; } = "";
    public string FameNickname { get; set; } = "";
    public decimal? FameLevel { get; set; } = 1;
    public bool FameShiny { get; set; }

    private List<FameTeamViewModel> BuildFame(IReadOnlyList<FameTeam> teams)
        => [.. teams.Select(t => new FameTeamViewModel(t, _sav!.Context, this))];

    private void ReloadFame(int? team = null, int? slot = null)
    {
        if (_sav is null)
            return;
        FameRows = BuildFame(HallOfFame.Load(_sav));
        Raise(nameof(FameRows));
        Raise(nameof(HasFameTeams));
        Raise(nameof(Summary));
        SelectedFameMember = team is null ? null
            : FameRows.FirstOrDefault(t => t.Index == team)?.Members.FirstOrDefault(m => m.Member.Slot == slot);
        ApplyFilter();
    }

    private void FameChanged(string message, int? team = null, int? slot = null)
    {
        Changed?.Invoke();
        ReloadFame(team, slot);
        _status(message + " Lembre-se de salvar.");
    }

    private void ApplyFameMember()
    {
        if (_sav is null || _selectedFame is not { } m)
            return;
        var name = (FameSpecies ?? "").Trim();
        int index = GameInfo.Strings.Species.ToList().FindIndex(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        if (index <= 0 || index > FameCaps.MaxSpecies)
        {
            _status($"Espécie não encontrada neste jogo: {name}.");
            return;
        }
        var nickname = (FameNickname ?? "").Trim();
        // Sem apelido: o nome da especie, em maiusculas no Gen 1-3 como nos jogos (no X/Y o servico usa o nome do jogo).
        if (FameHasNickname && nickname.Length == 0 && !FameHasShiny)
            nickname = GameInfo.Strings.Species[index].ToUpperInvariant();
        if (FameHasNickname && nickname.Length > FameCaps.NicknameLength)
            nickname = nickname[..FameCaps.NicknameLength];
        HallOfFame.SetMember(_sav, m.TeamIndex, m.Member.Slot, (ushort)index, nickname, (int)(FameLevel ?? 1), FameShiny);
        FameChanged($"Hall da Fama: {GameInfo.Strings.Species[index]} gravado em {m.TeamTitle}.", m.TeamIndex, m.Member.Slot);
    }

    private void RegisterFame()
    {
        if (_sav is null)
            return;
        if (_sav.PartyCount == 0)
        {
            _status("A equipe do save está vazia.");
            return;
        }
        HallOfFame.RegisterParty(_sav);
        FameChanged("Equipe atual registrada no Hall da Fama.");
    }

    internal async Task CopyPartyToFameAsync(FameTeamViewModel team)
    {
        if (_sav is null || _sav.PartyCount == 0)
            return;
        if (!await _confirm("Usar a equipe atual?", $"Os Pokémon de “{team.Title}” serão trocados pelos da equipe atual do save.", "Trocar"))
            return;
        HallOfFame.CopyParty(_sav, team.Index);
        FameChanged($"{team.Title}: trocada pela equipe atual.");
    }

    internal async Task DeleteFameAsync(FameTeamViewModel team)
    {
        if (_sav is null)
            return;
        if (!await _confirm("Apagar equipe do Hall da Fama?", $"“{team.Title}” sai do Hall da Fama. As equipes seguintes sobem uma posição.", "Apagar"))
            return;
        HallOfFame.DeleteTeam(_sav, team.Index);
        FameChanged($"{team.Title}: apagada do Hall da Fama.");
    }

    internal void SetFameDate(FameTeamViewModel team, DateTime date)
    {
        if (_sav is null)
            return;
        HallOfFame.SetDate(_sav, team.Index, date);
        Changed?.Invoke();
        _status($"{team.Title}: data trocada para {date:dd/MM/yyyy}. Lembre-se de salvar.");
    }

    /// <summary>Atalho de evento: pergunta, aplica e atualiza as flags/valores mostrados.</summary>
    private async Task RunShortcutAsync(GameShortcut s)
    {
        if (!await _confirm(s.Name, s.Description + " As flags e valores do evento são alterados no save; exporte uma cópia antes, se tiver dúvida.", "Aplicar"))
            return;
        try
        {
            BeforeShortcut?.Invoke();
            s.Apply();
        }
        catch (Exception ex)
        {
            _status($"{s.Name}: erro ({ex.Message})");
            return;
        }
        Changed?.Invoke();
        foreach (var f in _flags) f.Refresh();
        foreach (var w in _works) w.Refresh();
        foreach (var r in _shortcuts) r.Refresh();
        AfterShortcut?.Invoke();
        _status($"{s.Name}: aplicado. Lembre-se de salvar.");
    }

    /// <summary>Ativa ou desativa todas as flags que o filtro esta mostrando (ex.: todos os pontos de voo).</summary>
    private async Task SetVisibleAsync(bool value)
    {
        var rows = FlagRows.Where(f => f.IsSet != value).ToList();
        if (_tab != 0 || rows.Count == 0)
        {
            _status(value ? "Todas as flags mostradas já estão ativadas." : "Todas as flags mostradas já estão desativadas.");
            return;
        }
        var verb = value ? "Ativar" : "Desativar";
        if (!await _confirm($"{verb} {rows.Count} flags",
                $"{verb} as {rows.Count} flags mostradas pelo filtro atual? Flags de história fora de ordem podem travar eventos do jogo; prefira itens escondidos, pontos de voo e treinadores. Exporte uma cópia do save antes, se tiver dúvida.",
                verb))
            return;
        foreach (var f in rows)
            f.IsSet = value;
        _status($"{rows.Count} flags {(value ? "ativadas" : "desativadas")}. Lembre-se de salvar.");
        if (_onlySet)
            ApplyFilter();
    }
}

public sealed class FlagRowViewModel(GameFlag flag, GameEditors editors, Action changed) : ViewModelBase
{
    public int Index => flag.Index;
    public string Number => flag.Code ?? $"#{flag.Index:0000}";
    public string Code => flag.Code ?? "";
    public bool HasName => flag.Name.Length > 0;
    // Hash do Z-A (16 digitos) ja aparece na coluna do codigo; nao repete no nome
    public string Name => HasName ? flag.Name : flag.Code is { Length: > 8 } ? "Sem nome conhecido" : $"Flag {flag.Code ?? flag.Index.ToString()}";
    public string Category => GameEditors.CategoryOf(flag);
    public void Refresh() => Raise(nameof(IsSet));
    public bool IsSet
    {
        get => editors.GetFlag(flag.Index);
        set
        {
            if (value == IsSet)
                return;
            editors.SetFlag(flag.Index, value);
            changed();
            Raise();
        }
    }
}

public sealed class WorkRowViewModel : ViewModelBase
{
    private readonly GameWork _work;
    private readonly GameEditors _editors;
    private readonly Action _changed;

    public WorkRowViewModel(GameWork work, GameEditors editors, Action changed)
    {
        _work = work;
        _editors = editors;
        _changed = changed;
        Options = [.. work.Options.Where(o => !o.IsCustom).Select(o => new WorkOption(o.Name, o.Value))];
    }

    public int Index => _work.Index;
    public string Number => _work.Code ?? $"#{_work.Index:000}";
    public string Code => _work.Code ?? "";
    public bool HasName => _work.Name.Length > 0;
    public string Name => HasName ? _work.Name : _work.Code is { Length: > 8 } ? "Sem nome conhecido" : $"Valor {_work.Code ?? _work.Index.ToString()}";
    public string Category => GameEditors.CategoryOf(_work);
    public decimal Min => _work.Min;
    public decimal Max => _work.Max;
    public void Refresh() { Raise(nameof(ValueNumber)); Raise(nameof(SelectedOption)); }
    public IReadOnlyList<WorkOption> Options { get; }
    public bool HasOptions => Options.Count > 0;

    public long Value => _editors.GetWork(_work.Index);
    public decimal? ValueNumber
    {
        get => Value;
        set
        {
            if (value is null || (long)value == Value)
                return;
            _editors.SetWork(_work.Index, (long)value);
            _changed();
            Raise();
            Raise(nameof(SelectedOption));
        }
    }

    /// <summary>Valor conhecido (lista do PKHeX); null quando o valor atual nao tem nome.</summary>
    public WorkOption? SelectedOption
    {
        get => Options.FirstOrDefault(o => o.Value == Value);
        set
        {
            if (value is null)
                return;
            ValueNumber = value.Value;
        }
    }
}

/// <summary>Atalho de evento na pagina Jogo: nome, explicacao e se ainda da para usar.</summary>
public sealed class ShortcutRowViewModel(GameShortcut shortcut, Action run) : ViewModelBase
{
    public string Name => shortcut.Name;
    public string Description => shortcut.Description;
    public bool IsReady { get { try { return shortcut.Ready(); } catch { return false; } } }
    /// <summary>Quando nao da para usar: ja feito, ou o jogo ainda nao chegou la (ex.: revanche antes de capturar o lendario).</summary>
    public string State => IsReady ? "" : "Nada a fazer agora";
    public RelayCommand ApplyCommand { get; } = new(run);
    public void Refresh() { Raise(nameof(IsReady)); Raise(nameof(State)); }
}

/// <summary>Equipe do Hall da Fama na pagina Jogo.</summary>
public sealed class FameTeamViewModel : ViewModelBase
{
    private readonly FameTeam _team;
    private readonly GamePageViewModel _page;

    public FameTeamViewModel(FameTeam team, EntityContext context, GamePageViewModel page)
    {
        _team = team;
        _page = page;
        _date = team.When;
        Members = [.. team.Members.Select(m => new FameMemberViewModel(m, context, team, page))];
        CopyPartyCommand = new RelayCommand(() => _ = page.CopyPartyToFameAsync(this));
        DeleteCommand = new RelayCommand(() => _ = page.DeleteFameAsync(this));
    }

    public int Index => _team.Index;
    public string Title => _team.Title;
    public string Date => _team.Date;
    public bool HasDate => _team.Date.Length > 0;
    public bool CanEditDate => _page.FameHasDate;
    public IReadOnlyList<FameMemberViewModel> Members { get; }
    public RelayCommand CopyPartyCommand { get; }
    public RelayCommand DeleteCommand { get; }

    private DateTime? _date;
    /// <summary>Data da vitoria (X/Y e Omega Ruby/Alpha Sapphire): trocar grava no save.</summary>
    public DateTime? EditDate
    {
        get => _date;
        set
        {
            if (value is not { } d || d.Date == _date?.Date || d.Year is < 2000 or > 2255)
                return;
            _date = d.Date;
            _page.SetFameDate(this, d.Date);
            Raise();
        }
    }
}

public sealed class FameMemberViewModel(FameMember member, EntityContext context, FameTeam team, GamePageViewModel page) : ViewModelBase
{
    public FameMember Member => member;
    public int TeamIndex => team.Index;
    public string TeamTitle => team.Title;
    private RelayCommand? _select;
    /// <summary>Clique no Pokemon: abre o editor acima das equipes.</summary>
    public RelayCommand SelectCommand => _select ??= new(() => page.SelectedFameMember = this);
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private Avalonia.Media.Imaging.Bitmap? _sprite;
    private bool _loaded;
    public Avalonia.Media.Imaging.Bitmap? Sprite
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _sprite = SpriteService.GetSpeciesSprite(member.Species, member.Shiny, member.Form, member.Gender, context);
            }
            return _sprite;
        }
    }
    public string Name => member.Nickname;
    public string Level => member.Level > 0 ? $"Nv. {member.Level}" : "";
    public bool Shiny => member.Shiny;
    public string Tip => (member.Species < GameInfo.Strings.Species.Count ? GameInfo.Strings.Species[member.Species] : "") + (member.Shiny ? " ★" : "");
}

public sealed record WorkOption(string Name, ushort Value)
{
    public override string ToString() => $"{Name} ({Value})";
}

public sealed class RecordRowViewModel(GameRecord record, GameEditors editors, Action changed) : ViewModelBase
{
    public int Id => record.Id;
    public string Number => $"#{record.Id:000}";
    public bool HasName => record.Name.Length > 0;
    public string Name => HasName ? record.Name : $"Recorde {record.Id}";
    public decimal Max => record.Max;
    public long Value => editors.GetRecord(record);
    public decimal? ValueNumber
    {
        get => Value;
        set
        {
            if (value is null || (long)value == Value)
                return;
            editors.SetRecord(record, (long)value);
            changed();
            Raise();
        }
    }
}
