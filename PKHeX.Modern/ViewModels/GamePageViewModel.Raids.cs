using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    public bool HasRaids => RaidRegions.Count != 0;
    public bool IsRaidsTab => Tab == 6;
    public IReadOnlyList<RaidRegionViewModel> RaidRegions { get; private set; } = [];
    private RaidRegionViewModel? _raidRegion;
    public RaidRegionViewModel? RaidRegion
    {
        get => _raidRegion;
        set { if (Set(ref _raidRegion, value)) { SelectedRaid = value?.Rows.FirstOrDefault(); Raise(nameof(ActivateRaidsCommand)); Raise(nameof(DeactivateRaidsCommand)); } }
    }
    private RaidRowViewModel? _selectedRaid;
    public RaidRowViewModel? SelectedRaid { get => _selectedRaid; set => Set(ref _selectedRaid, value); }
    public const string RaidsExplanation = "A seed e o progresso do jogo definem o Pokémon. Não há busca de seed por Pokémon. As alterações ficam em memória até Salvar e não têm desfazer.";
    public string RaidsNote => RaidsExplanation;
    public RelayCommand ActivateRaidsCommand => new(() => _ = SetAllRaidsAsync(true), () => RaidRegion?.CanActivate == true);
    public RelayCommand DeactivateRaidsCommand => new(() => _ = SetAllRaidsAsync(false), () => RaidRegion?.CanActivate == true);
    private void RefreshRaids()
    {
        var regions = new List<RaidRegionViewModel>();
        void ChangedRaid() => Changed?.Invoke();
        void Add8(string name, RaidSpawnList8 list) { if (list.CountAll > 0) regions.Add(new(name, list, ChangedRaid, _status)); }
        void Add9(string name, RaidSpawnList9 list) { if (list.CountAll > 0) regions.Add(new(name, list, ChangedRaid, _status)); }
        if (_sav is SAV8SWSH swsh) { Add8("Galar", swsh.RaidGalar); Add8("Isle of Armor", swsh.RaidArmor); Add8("Crown Tundra", swsh.RaidCrown); }
        if (_sav is SAV9SV sv)
        {
            Add9("Paldea", sv.RaidPaldea); Add9("Kitakami", sv.RaidKitakami); Add9("Blueberry Academy", sv.RaidBlueberry);
            if (sv.RaidSevenStar.CountAll > 0) regions.Add(new("Registros de 7 estrelas", sv.RaidSevenStar, ChangedRaid, _status));
        }
        RaidRegions = regions; RaidRegion = regions.FirstOrDefault(); Raise(nameof(RaidRegions)); Raise(nameof(HasRaids));
    }
    public async Task SetAllRaidsAsync(bool active)
    {
        if (_sav is null || RaidRegion is not { CanActivate: true } region) return;
        var sav = _sav;
        if (!await _confirm(active ? "Ativar raids da região?" : "Desativar raids da região?",
            "Todas as raids válidas da região selecionada serão alteradas. Esta edição não tem desfazer.", active ? "Ativar" : "Desativar") || !ReferenceEquals(sav, _sav)) return;
        region.SetAll(active); Changed?.Invoke(); _status("Raids alteradas. Salve para gravar.");
    }
}

public sealed class RaidRegionViewModel : ViewModelBase
{
    private readonly RaidSpawnList9? _list9;
    private readonly Action _changed;
    private readonly Action<string> _status;
    public string Name { get; }
    public IReadOnlyList<RaidRowViewModel> Rows { get; }
    public bool CanActivate { get; }
    public bool HasSeeds => _list9?.HasSeeds == true;
    public RaidRegionViewModel(string name, RaidSpawnList8 list, Action changed, Action<string> status)
    {
        Name = name; _changed = changed; _status = status; CanActivate = true;
        Rows = Enumerable.Range(0, Math.Min(list.CountAll, list.CountUsed)).Select(i => new RaidRowViewModel(i, list.GetRaid(i), changed, status)).ToArray();
    }
    public RaidRegionViewModel(string name, RaidSpawnList9 list, Action changed, Action<string> status)
    {
        Name = name; _list9 = list; _changed = changed; _status = status; CanActivate = true;
        Rows = Enumerable.Range(0, Math.Min(list.CountAll, list.CountUsed)).Select(i => new RaidRowViewModel(i, list.GetRaid(i), changed, status)).ToArray();
    }
    public RaidRegionViewModel(string name, RaidSevenStar9 list, Action changed, Action<string> status)
    {
        Name = name; _changed = changed; _status = status;
        Rows = Enumerable.Range(0, list.CountAll).Select(i => new RaidRowViewModel(i, list.GetRaid(i), changed, status)).ToArray();
    }
    public string CurrentSeed { get => _list9?.CurrentSeed.ToString("X16") ?? ""; set { if (HasSeeds && RaidRowViewModel.Hex(value, ulong.MaxValue, _status, out var n)) { _list9!.CurrentSeed = n; _changed(); Raise(); } } }
    public string TomorrowSeed { get => _list9?.TomorrowSeed.ToString("X16") ?? ""; set { if (HasSeeds && RaidRowViewModel.Hex(value, ulong.MaxValue, _status, out var n)) { _list9!.TomorrowSeed = n; _changed(); Raise(); } } }
    public void SetAll(bool active)
    {
        foreach (var row in Rows)
        {
            // Preserva a toca especial do Core e cristais sem posicao no mapa.
            if (row.IsSwsh && row.Index == 16 || row.IsSv && !row.HasPosition) continue;
            row.Active = active;
        }
    }
}

public sealed class RaidRowViewModel : ViewModelBase
{
    private readonly RaidSpawnDetail? _raid8;
    private readonly TeraRaidDetail? _raid9;
    private readonly SevenStarRaidDetail? _seven;
    private readonly Action _changed;
    private readonly Action<string> _status;
    public RaidRowViewModel(int index, RaidSpawnDetail raid, Action changed, Action<string> status) { Index = index; _raid8 = raid; _changed = changed; _status = status; }
    public RaidRowViewModel(int index, TeraRaidDetail raid, Action changed, Action<string> status) { Index = index; _raid9 = raid; _changed = changed; _status = status; }
    public RaidRowViewModel(int index, SevenStarRaidDetail raid, Action changed, Action<string> status) { Index = index; _seven = raid; _changed = changed; _status = status; }
    public int Index { get; }
    public bool IsSwsh => _raid8 is not null;
    public bool IsSv => _raid9 is not null;
    public bool IsSpawn => _seven is null;
    public bool IsSeven => _seven is not null;
    public bool HasPosition => _raid9?.AreaID > 0;
    public string Title => $"#{Index + 1} · {(IsSeven ? Identifier.ToString(CultureInfo.InvariantCulture) : Active ? "Ativa" : "Inativa")}";
    public string StarsText => IsSwsh ? $"{Stars} ★" : _raid9?.Content switch { TeraRaidContentType.Base05 => "1–5 ★", TeraRaidContentType.Black6 => "6 ★", TeraRaidContentType.Might7 => "7 ★", TeraRaidContentType.Distribution => "Evento", _ => "" };
    public string Position => _raid9?.ScenePointName ?? "";
    public bool Active
    {
        get => _raid8?.IsActive ?? _raid9?.IsEnabled ?? false;
        set
        {
            if (value == Active) return;
            if (_raid8 is { } r) { if (value) r.Activate((byte)Math.Clamp(r.Stars, (byte)0, (byte)4), (byte)Math.Clamp(r.RandRoll, (byte)1, (byte)100)); else r.Deactivate(); }
            if (_raid9 is { } t) t.IsEnabled = value;
            Changed();
        }
    }
    public IReadOnlyList<string> Types => IsSwsh ? ["Inativa", "Normal", "Rara", "Normal (Wishing Piece)", "Rara (Wishing Piece)", "Evento", "Dynamax Crystal"] : ["Normal (1–5 estrelas)", "Cristal preto (6 estrelas)", "Evento", "Evento (7 estrelas)"];
    public int TypeIndex { get => _raid8 is { } r ? (int)r.DenType : (int)(_raid9?.Content ?? 0); set { if (value < 0 || value >= Types.Count) return; if (_raid8 is { } r) r.DenType = (RaidType)value; if (_raid9 is { } t) t.Content = (TeraRaidContentType)value; Changed(); } }
    public int Stars { get => (_raid8?.Stars ?? 0) + 1; set { if (_raid8 is { } r && value is >= 1 and <= 5) { r.Stars = (byte)(value - 1); Changed(); } } }
    public int Roll { get => _raid8?.RandRoll ?? 0; set { if (_raid8 is { } r && value is >= 1 and <= 100) { r.RandRoll = (byte)value; Changed(); } } }
    public string Seed { get => _raid8?.Seed.ToString("X16") ?? _raid9?.Seed.ToString("X8") ?? ""; set { if (Hex(value, IsSv ? uint.MaxValue : ulong.MaxValue, _status, out var n)) { if (_raid8 is { } r) r.Seed = n; if (_raid9 is { } t) t.Seed = (uint)n; Changed(); } } }
    public string Hash { get => _raid8?.Hash.ToString("X16") ?? ""; set { if (_raid8 is { } r && Hex(value, ulong.MaxValue, _status, out var n)) { r.Hash = n; Changed(); } } }
    public bool Watts { get => _raid8?.WattsHarvested == true; set { if (_raid8 is { } r) { r.WattsHarvested = value; Changed(); } } }
    public bool Event { get => _raid8?.IsEvent == true; set { if (_raid8 is { } r) { r.IsEvent = value; Changed(); } } }
    public bool LeaguePoints { get => _raid9?.IsClaimedLeaguePoints == true; set { if (_raid9 is { } r) { r.IsClaimedLeaguePoints = value; Changed(); } } }
    public uint Identifier { get => _seven?.Identifier ?? 0; set { if (_seven is { } r) { r.Identifier = value; Changed(); } } }
    public bool Captured { get => _seven?.Captured == true; set { if (_seven is { } r) { r.Captured = value; Changed(); } } }
    public bool Defeated { get => _seven?.Defeated == true; set { if (_seven is { } r) { r.Defeated = value; Changed(); } } }
    private void Changed() { _changed(); Raise(string.Empty); }
    internal static bool Hex(string text, ulong max, Action<string> status, out ulong value)
    {
        var s = text.Trim(); if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (ulong.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value) && value <= max) return true;
        status("Seed ou hash inválido: use hexadecimal dentro do limite do campo."); return false;
    }
}
