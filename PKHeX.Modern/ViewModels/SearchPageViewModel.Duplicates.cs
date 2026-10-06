using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class SearchPageViewModel
{
    private DuplicateAuditResult? _auditResult;
    private bool _duplicates;
    public bool ShowDuplicates { get => _duplicates; set { if (Set(ref _duplicates, value)) { Raise(nameof(ShowSearch)); SelectedDuplicate = null; UpdateDuplicates(); } } }
    public bool ShowSearch => !ShowDuplicates;
    public RelayCommand ShowSearchCommand => new(() => ShowDuplicates = false);
    public RelayCommand ShowDuplicatesCommand => new(() => ShowDuplicates = true);
    public RelayCommand BackDuplicatesCommand => new(() => SelectedDuplicate = null);
    public IReadOnlyList<DuplicateGroupViewModel> DuplicateGroups { get; private set; } = [];
    private DuplicateGroupViewModel? _duplicate;
    public DuplicateGroupViewModel? SelectedDuplicate { get => _duplicate; set { if (Set(ref _duplicate, value)) { Raise(nameof(HasDuplicateDetail)); Raise(nameof(ShowDuplicateList)); } } }
    public bool HasDuplicateDetail => SelectedDuplicate is not null;
    public bool ShowDuplicateList => !HasDuplicateDetail;
    public string DuplicateSummary { get; private set; } = "";
    public Func<IReadOnlyList<string>, Task>? ShowComparison { get; set; }

    private void UpdateDuplicates()
    {
        if (!ShowDuplicates) return;
        var result = _auditResult ?? new DuplicateAuditResult([], new Dictionary<string, string>());
        var source = _source > 0 && _source <= _sources.Count ? _sources[_source - 1] : null;
        var groups = result.Groups.Where(g => g.Entries.Any(e => (source is null || e.Source.Id == source.Id)
            && (_query.Length == 0 || e.Text.Contains(_query, StringComparison.OrdinalIgnoreCase)))).ToArray();
        DuplicateGroups = groups.Select(Group).ToArray();
        SelectedDuplicate = null;
        DuplicateSummary = string.Format(Loc.T("{0} grupos · {1} exemplares · mesmos dados armazenados, byte a byte"), groups.Count(g => !g.IsBackupPair) + groups.Sum(g => g.Children?.Count ?? 0),
            groups.SelectMany(g => g.Entries).DistinctBy(e => e.LocationId).Count());
        Raise(nameof(DuplicateGroups)); Raise(nameof(DuplicateSummary));
    }
    private DuplicateGroupViewModel Group(DuplicateGroup group)
        => new(group, () => SelectedDuplicate = Group(group), entry => _open(entry), child => SelectedDuplicate = Group(child),
            async () =>
            {
                if (!group.CanCompare || ShowComparison is null) return;
                var a = group.Entries[0]; var b = group.Entries.First(e => !StoredPokemon.Equal(e.Pkm, a.Pkm));
                await ShowComparison([a.Source.Name + " · " + a.Where + " → " + b.Source.Name + " · " + b.Where, .. PokemonDiff.Details(a.Pkm, b.Pkm)]);
            });
}

public sealed class DuplicateGroupViewModel
{
    public DuplicateGroupViewModel(DuplicateGroup group, Action select, Func<DbEntry, Task> open, Action<DuplicateGroup> selectChild, Func<Task> compare)
    {
        Group = group; SelectCommand = new RelayCommand(select); CompareCommand = new RelayCommand(() => _ = compare());
        Members = group.Entries.Select(e => new DuplicateMemberViewModel(e, new RelayCommand(() => _ = open(e)))).ToArray();
        Children = group.Children?.Select(g => new DuplicateGroupViewModel(g, () => selectChild(g), open, selectChild, compare)).ToArray() ?? [];
    }
    public DuplicateGroup Group { get; }
    public string Title => Group.Title;
    public string Category => Loc.T(Group.Kind);
    public string Count => Group.IsBackupPair ? string.Format(Loc.T("{0} Pokémon iguais entre os arquivos"), Group.MatchedCount)
        : string.Format(Loc.T("{0} exemplares"), Group.Entries.Count);
    public bool HasChildren => Group.IsBackupPair;
    public bool ShowMembers => !HasChildren;
    public bool CanCompare => Group.CanCompare;
    public IReadOnlyList<DuplicateMemberViewModel> Members { get; }
    public IReadOnlyList<DuplicateGroupViewModel> Children { get; }
    public RelayCommand SelectCommand { get; }
    public RelayCommand CompareCommand { get; }
}
public sealed class DuplicateMemberViewModel(DbEntry entry, RelayCommand open)
{
    public DbEntry Entry => entry;
    public string Name => entry.Species + (entry.Nickname.Length > 0 ? " · " + entry.Nickname : "");
    public string Location => entry.Source.Name + (entry.Source.IsBank ? "" : " · " + ZipSaves.DisplayName(entry.Source.Id)) + " · " + entry.Where;
    public string Details => string.Join(" · ", entry.Pkm.CurrentLevel, entry.Nature, entry.Ability, entry.Ball, entry.OT);
    public Bitmap? Sprite => SpriteService.GetSprite(entry.Pkm);
    public RelayCommand OpenCommand => open;
}
