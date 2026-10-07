using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Uma pagina da barra lateral. Para adicionar uma nova funcao:
/// crie uma classe derivada, registre em <see cref="MainViewModel"/> e um DataTemplate em App.axaml.
/// </summary>
public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public abstract string Icon { get; }
    /// <summary>Atalho exibido na barra lateral (Ctrl+1, Ctrl+2...). Definido pelo MainViewModel.</summary>
    public string Shortcut { get; set; } = "";
    /// <summary>Chamado quando a pagina altera o save (marca alteracoes nao exportadas).</summary>
    public Action? Changed { get; set; }
    /// <summary>Indica se a pagina se aplica ao save atual (ex.: jogos sem mochila).</summary>
    public virtual bool IsAvailable => true;
    public abstract void Load(SaveFile sav);
}

/// <summary>Base para paginas que exibem slots de Pokemon (caixas e equipe).</summary>
public abstract class SlotPageViewModel(Action<SlotViewModel> select) : PageViewModel
{
    public ObservableCollection<SlotViewModel> Slots { get; } = [];
    /// <summary>Chamado depois que os slots sao recarregados (a busca global reaplica o destaque).</summary>
    public Action? SlotsLoaded { get; set; }
    public RelayCommand SelectSlotCommand { get; } = new(p => { if (p is SlotViewModel s) select(s); });
    /// <summary>Colar/copiar varios sets Showdown (equipe e caixas; definidos pelo MainViewModel).</summary>
    public RelayCommand? PasteShowdownCommand { get; set; }
    public RelayCommand? CopyShowdownCommand { get; set; }
    public RelayCommand? ExportReportCommand { get; set; }
}

public sealed class BoxesPageViewModel : SlotPageViewModel
{
    private SaveFile? _sav;
    private bool _loading;

    public BoxesPageViewModel(Action<SlotViewModel> select) : base(select)
    {
        PreviousBoxCommand = new RelayCommand(() => CurrentBox--, () => CurrentBox > 0);
        NextBoxCommand = new RelayCommand(() => CurrentBox++, () => _sav is not null && CurrentBox < _sav.BoxCount - 1);
    }

    /// <summary>Faixa da equipe exibida acima das caixas (permite arrastar entre caixa e equipe).</summary>
    public PartyPageViewModel? Party { get; init; }
    public bool ShowParty => Party is { Slots.Count: > 0 };

    /// <summary>Ordenar (definido pelo MainViewModel, que guarda o desfazer): criterio e se vale para todas as caixas.</summary>
    public Action<CoreAdapter.BoxSortOption, bool>? Sort { get; set; }
    /// <summary>Criterios do menu "Ordenar" (cada um com "esta caixa" e "todas as caixas").</summary>
    public IReadOnlyList<SortOptionViewModel> SortOptions { get; private set; } = [];

    public override string Title => "Caixas";
    public override string Icon => "▦";
    public RelayCommand PreviousBoxCommand { get; }
    public RelayCommand NextBoxCommand { get; }
    public Func<string, string, string, System.Threading.Tasks.Task<string?>>? Prompt { get; set; }
    public bool CanRename => _sav is IBoxDetailName;
    public bool HasWallpapers => Wallpapers.Count > 0;
    public IReadOnlyList<string> Wallpapers { get; private set; } = [];
    public Avalonia.Media.Imaging.Bitmap? Wallpaper { get; private set; }
    private int _wallpaperIntensity;
    public int WallpaperIntensity { get => _wallpaperIntensity; set { if (Set(ref _wallpaperIntensity, value)) Raise(nameof(WallpaperOpacity)); } }
    public double WallpaperOpacity => WallpaperIntensity switch { 1 => 0.10, 2 => 0, _ => 0.25 };
    public RelayCommand RenameCommand => new(async () =>
    {
        if (_sav is not IBoxDetailName names || Prompt is null) return;
        var sav = _sav;
        int box = CurrentBox, max = CoreAdapter.GetBoxNameLength(sav);
        var name = await Prompt("Renomear caixa", $"Nome da caixa (até {max} caracteres):", BoxName);
        while (name is { } && name.Length > max && sav == _sav)
            name = await Prompt("Nome muito longo", $"Este jogo aceita até {max} caracteres. Escolha um nome menor:", name);
        if (name is null || sav != _sav || names.GetBoxName(box) == name) return;
        names.SetBoxName(box, name);
        RefreshNames();
        Changed?.Invoke();
    });
    public int WallpaperIndex
    {
        get => _sav is IBoxDetailWallpaper wp && HasWallpapers ? wp.GetBoxWallpaper(CurrentBox) : -1;
        set
        {
            if (_loading || _sav is not IBoxDetailWallpaper wp || value < 0 || value >= Wallpapers.Count || value == WallpaperIndex) return;
            wp.SetBoxWallpaper(CurrentBox, value);
            RefreshWallpaper();
            Changed?.Invoke();
        }
    }
    private void RefreshNames()
    {
        if (_sav is null) return;
        for (int i = 0; i < BoxTabs.Count; i++) BoxTabs[i].Name = CoreAdapter.GetBoxName(_sav, i);
        Raise(nameof(BoxName));
    }
    private void RefreshWallpaper()
    {
        var previous = Wallpaper;
        Wallpaper = _sav is not null && HasWallpapers ? SpriteService.GetBoxWallpaper(_sav, CurrentBox) : null;
        Raise(nameof(Wallpaper));
        Raise(nameof(WallpaperIndex));
        previous?.Dispose();
    }

    private int _currentBox;
    public int CurrentBox
    {
        get => _currentBox;
        set
        {
            if (_sav is null || value < 0 || value >= _sav.BoxCount || !Set(ref _currentBox, value))
                return;
            LoadBox();
        }
    }

    public string BoxName => _sav is null ? "" : CoreAdapter.GetBoxName(_sav, CurrentBox);

    /// <summary>Abas com o nome de cada caixa (clique ou pare em cima durante o arraste para trocar).</summary>
    public ObservableCollection<BoxTabViewModel> BoxTabs { get; } = [];
    public int FilledCount => Slots.Count(s => !s.IsEmpty);
    public string BoxLabel => _sav is null ? "" : $"Caixa {CurrentBox + 1} de {_sav.BoxCount} · {FilledCount}/{Slots.Count} Pokémon";

    public override void Load(SaveFile sav)
    {
        _loading = true;
        _sav = sav;
        _currentBox = 0;
        Wallpapers = CoreAdapter.GetBoxWallpapers(sav);
        Raise(nameof(Wallpapers));
        Raise(nameof(HasWallpapers));
        Raise(nameof(CanRename));
        BoxTabs.Clear();
        for (int i = 0; i < sav.BoxCount; i++)
        {
            int box = i;
            BoxTabs.Add(new BoxTabViewModel(CoreAdapter.GetBoxName(sav, i), new RelayCommand(() => CurrentBox = box)));
        }
        SortOptions = [.. CoreAdapter.GetBoxSortOptions(sav).Select(o => new SortOptionViewModel(o.Name,
            new RelayCommand(() => Sort?.Invoke(o, false)), new RelayCommand(() => Sort?.Invoke(o, true))))];
        Raise(nameof(SortOptions));
        Raise(nameof(CurrentBox));
        LoadBox();
        Raise(nameof(ShowParty));
        _loading = false;
    }

    public void Reload() => LoadBox();

    private void LoadBox()
    {
        if (_sav is null)
            return;
        bool wasLoading = _loading;
        _loading = true;
        Slots.Clear();
        for (int i = 0; i < _sav.BoxSlotCount; i++)
        {
            var s = new SlotViewModel(CurrentBox, i);
            s.Load(_sav);
            Slots.Add(s);
        }
        Raise(nameof(BoxName));
        Raise(nameof(BoxLabel));
        RefreshNames();
        RefreshWallpaper();
        for (int i = 0; i < BoxTabs.Count; i++)
            BoxTabs[i].IsCurrent = i == CurrentBox;
        SlotsLoaded?.Invoke();
        PreviousBoxCommand.NotifyCanExecuteChanged();
        NextBoxCommand.NotifyCanExecuteChanged();
        _loading = wasLoading;
    }
}

public sealed partial class PartyPageViewModel(Action<SlotViewModel> select) : SlotPageViewModel(select)
{
    private SaveFile? _sav;
    public override string Title => "Equipe";
    public override string Icon => "◉";
    public override bool IsAvailable => _sav is null || CoreAdapter.GetPartyCount(_sav) > 0 || Daycares.All(_sav).Length > 0;

    public override void Load(SaveFile sav)
    {
        _sav = sav;
        Slots.Clear();
        for (int i = 0; i < CoreAdapter.GetPartyCount(sav); i++)
        {
            var s = new SlotViewModel(SlotViewModel.Party, i);
            s.Load(sav);
            Slots.Add(s);
        }
        SlotsLoaded?.Invoke();
        Raise(nameof(IsAvailable));
        RefreshDaycare();
    }
}

public sealed class TrainerPageViewModel : PageViewModel
{
    private SaveFile? _sav;
    public override string Title => "Treinador";
    public override string Icon => "♟";

    public override void Load(SaveFile sav)
    {
        _sav = sav;
        Raise(string.Empty);
    }

    public string Game => _sav is null ? "" : CoreAdapter.GetGameName(_sav);
    public string Generation => _sav is null ? "" : $"Geração {_sav.Generation}";
    public string Checksum => _sav is null ? "" : _sav.ChecksumsValid ? "Válidos" : _sav.ChecksumInfo;
    public int MaxMoney => _sav?.MaxMoney ?? 0;

    public string OT { get => _sav?.OT ?? ""; set { if (_sav is not null) { _sav.OT = value; Changed?.Invoke(); } Raise(); } }
    public decimal TID { get => _sav?.DisplayTID ?? 0; set { if (_sav is not null) { _sav.DisplayTID = (uint)value; Changed?.Invoke(); } Raise(); } }
    public decimal SID { get => _sav?.DisplaySID ?? 0; set { if (_sav is not null) { _sav.DisplaySID = (uint)value; Changed?.Invoke(); } Raise(); } }
    public decimal Money { get => _sav?.Money ?? 0; set { if (_sav is not null) { _sav.Money = (uint)Math.Min(value, MaxMoney); Changed?.Invoke(); } Raise(); } }
    public decimal Hours { get => _sav?.PlayedHours ?? 0; set { if (_sav is not null) { _sav.PlayedHours = (int)value; Changed?.Invoke(); } Raise(); } }
    public decimal Minutes { get => _sav?.PlayedMinutes ?? 0; set { if (_sav is not null) { _sav.PlayedMinutes = (int)value; Changed?.Invoke(); } Raise(); } }
    public decimal Seconds { get => _sav?.PlayedSeconds ?? 0; set { if (_sav is not null) { _sav.PlayedSeconds = (int)value; Changed?.Invoke(); } Raise(); } }
}

public sealed class BagPageViewModel(Action<string> status) : PageViewModel
{
    private SaveFile? _sav;
    private PlayerBag? _bag;

    public override string Title => "Mochila";
    public override string Icon => "🎒";
    public override bool IsAvailable => _bag is null || Pouches.Count > 0;

    public ObservableCollection<PouchViewModel> Pouches { get; } = [];

    private PouchViewModel? _selected;
    public PouchViewModel? SelectedPouch { get => _selected; set => Set(ref _selected, value); }

    public RelayCommand SaveCommand => new(Save);

    /// <summary>Itens mudados na tela que ainda nao foram gravados no save (Salvar aplica antes de gravar).</summary>
    public bool HasPendingChanges { get; private set; }

    public override void Load(SaveFile sav)
    {
        _sav = sav;
        HasPendingChanges = false;
        Pouches.Clear();
        try
        {
            _bag = CoreAdapter.GetBag(sav);
            FillPouches(sav, _bag);
        }
        catch (Exception ex)
        {
            status($"Mochila indisponível: {ex.Message}");
        }
        SelectedPouch = Pouches.FirstOrDefault();
        Raise(nameof(IsAvailable));
        GiveAllTMsCommand.NotifyCanExecuteChanged();
    }

    private void FillPouches(SaveFile sav, PlayerBag bag)
    {
        bool editable = CoreAdapter.IsBagItemIdEditable(sav);
        foreach (var p in bag.Pouches)
            Pouches.Add(new PouchViewModel(bag, p, editable, CoreAdapter.GetItemNames(sav), sav.Context, sav.Version, OnItemChanged));
    }

    /// <summary>Todos os TMs (e HMs/TRs do mesmo bolso) na quantidade maxima, sem apagar o que ja estava la.</summary>
    public RelayCommand GiveAllTMsCommand => _giveAllTMs ??= new(GiveAllTMs, () => _bag?.Pouches.Any(p => p.Type == InventoryType.TMHMs) == true);
    private RelayCommand? _giveAllTMs;

    private void GiveAllTMs()
    {
        if (_sav is null || _bag is null)
            return;
        int given = 0, full = 0;
        foreach (var pouch in _bag.Pouches.Where(p => p.Type == InventoryType.TMHMs))
        {
            foreach (var id in pouch.GetAllItems())
            {
                if (id == 0 || !_bag.IsLegal(pouch.Type, id, 1))
                    continue;
                if (pouch.GiveItem(_bag, id, 1) < 0)
                    full++;
                else
                    given++;
            }
            // 99 de cada (ou o maximo do jogo, se for menor: na Gen 5+ o TM nao se gasta e fica 1), em ordem numerica.
            foreach (var item in pouch.Items.Where(i => i.Index != 0))
                item.Count = _bag.Clamp(pouch.Type, item.Index, 99);
            pouch.SortByIndex();
        }
        var index = SelectedPouch is { } sel ? Pouches.IndexOf(sel) : 0;
        Pouches.Clear();
        FillPouches(_sav, _bag);
        SelectedPouch = Pouches.FirstOrDefault(p => p.Name == nameof(InventoryType.TMHMs)) ?? Pouches.ElementAtOrDefault(index);
        OnItemChanged();
        status(full > 0
            ? $"{given} TM(s) na mochila, com 99 de cada; {full} não couberam (bolso cheio). Grave a mochila e salve o save para manter."
            : $"{given} TM(s) na mochila, com 99 de cada. Grave a mochila e salve o save para manter.");
    }

    /// <summary>Mexeu num item: o save passa a ter alteracoes pendentes (antes, o Salvar gravava sem a mochila).</summary>
    private void OnItemChanged()
    {
        HasPendingChanges = true;
        Changed?.Invoke();
    }

    /// <summary>Grava no save os itens mudados na tela, se houver (chamado pelo Salvar e ao trocar de aba).</summary>
    public bool ApplyPending()
    {
        if (!HasPendingChanges || _sav is null || _bag is null)
            return false;
        CoreAdapter.SaveBag(_sav, _bag);
        HasPendingChanges = false;
        return true;
    }

    private void Save()
    {
        if (_sav is null || _bag is null)
            return;
        CoreAdapter.SaveBag(_sav, _bag);
        HasPendingChanges = false;
        Changed?.Invoke();
        status("Mochila gravada. Lembre-se de exportar o save.");
    }
}

public sealed class PouchViewModel : ViewModelBase
{
    /// <param name="names">Nomes na numeracao do save (Gen 1-3 tem numeracao propria).</param>
    public PouchViewModel(PlayerBag bag, InventoryPouch pouch, bool editable, IReadOnlyList<string> names, EntityContext context, GameVersion version = GameVersion.Any, Action? changed = null)
    {
        Name = pouch.Type.ToString();
        Options = [.. pouch.GetAllItems().ToArray().Prepend((ushort)0).Distinct()
            .Select(id => new ItemOption(id, id == 0 ? "(nenhum)" : id < names.Count && names[id].Length > 0 ? names[id] : $"Item #{id}", context, version))];
        Items = [.. pouch.Items.Select(it => new BagItemViewModel(bag, pouch.Type, it, Options, editable, changed))];
    }

    public string Name { get; }
    public IReadOnlyList<ItemOption> Options { get; }
    public IReadOnlyList<BagItemViewModel> Items { get; }
}

/// <summary>Um item da lista da mochila; o icone e gerado so quando aparece na tela.</summary>
public sealed record ItemOption(int Id, string Name, EntityContext Context = EntityContext.None, GameVersion Version = GameVersion.Any)
{
    public Avalonia.Media.Imaging.Bitmap? Icon => SpriteService.GetItemSprite(Id, Context);
    /// <summary>Descricao e onde conseguir (AllGenWiki), quando houver.</summary>
    public string? Tip => Id == 0 ? null : ItemInfo.GetTooltip(Name, Context.Generation, Version);
    public override string ToString() => Name;
}

public sealed class BagItemViewModel(PlayerBag bag, InventoryType type, InventoryItem item, IReadOnlyList<ItemOption> options, bool editable, Action? changed = null) : ViewModelBase
{
    public IReadOnlyList<ItemOption> Options { get; } = options;
    public bool IsEditable { get; } = editable;

    public ItemOption? Selected
    {
        get => Options.FirstOrDefault(o => o.Id == item.Index);
        set
        {
            if (value is null || value.Id == item.Index)
                return;
            item.Index = value.Id;
            if (value.Id == 0)
                item.Count = 0;
            changed?.Invoke();
            Raise();
            Raise(nameof(Count));
            Raise(nameof(Icon));
            Raise(nameof(IsEmptySlot));
            Raise(nameof(Tip));
        }
    }

    public Avalonia.Media.Imaging.Bitmap? Icon => Selected?.Icon;
    /// <summary>Slot sem item (cartao apagado na grade da mochila).</summary>
    public bool IsEmptySlot => Selected is null or { Id: 0 };
    public string? Tip => Selected?.Tip;

    public decimal Count
    {
        get => item.Count;
        set
        {
            var count = item.Index == 0 ? 0 : bag.Clamp(type, item.Index, (int)value);
            if (count == item.Count)
                return;
            item.Count = count;
            changed?.Invoke();
            Raise();
        }
    }
}

/// <summary>Uma aba de caixa no topo da pagina Caixas.</summary>
public sealed class BoxTabViewModel(string name, RelayCommand go) : ViewModelBase
{
    private string _name = name;
    public string Name { get => _name; set => Set(ref _name, value); }
    public RelayCommand GoCommand { get; } = go;
    private bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
}

/// <summary>Um criterio do menu "Ordenar".</summary>
public sealed class SortOptionViewModel(string name, RelayCommand sortCurrent, RelayCommand? sortAll = null)
{
    public string Name { get; } = name;
    public RelayCommand SortCurrentCommand { get; } = sortCurrent;
    public RelayCommand? SortAllCommand { get; } = sortAll;
}
