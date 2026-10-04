using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class PartyPageViewModel
{
    public Func<DaycareSlotViewModel, Task>? EditDaycare { get; set; }
    public Func<DaycareSlotViewModel, Task>? DepositDaycare { get; set; }
    public Func<DaycareSlotViewModel, Task>? WithdrawDaycare { get; set; }
    public Action<string>? DaycareStatus { get; set; }
    public IReadOnlyList<DaycareAreaViewModel> DaycareAreas { get; private set; } = [];
    public bool HasDaycare => DaycareAreas.Count > 0;
    public void RefreshDaycare()
    {
        DaycareAreas = _sav is null ? [] : [.. Daycares.All(_sav).Select((d, i) => new DaycareAreaViewModel(_sav, d, i,
            () => Changed?.Invoke(), s => DaycareStatus?.Invoke(s),
            s => { if (EditDaycare is { } edit) _ = edit(s); }, s => { if (DepositDaycare is { } put) _ = put(s); }, s => { if (WithdrawDaycare is { } take) _ = take(s); }))];
        Raise(nameof(DaycareAreas)); Raise(nameof(HasDaycare));
    }
}

public sealed class DaycareAreaViewModel : ViewModelBase
{
    private readonly IDaycareStorage _storage; private readonly Action _changed; private readonly Action<string> _status;
    public string Title { get; }
    public IReadOnlyList<DaycareSlotViewModel> Slots { get; }
    public bool HasEgg => _storage is IDaycareEggState;
    public bool EggAvailable
    {
        get => _storage is IDaycareEggState { IsEggAvailable: true };
        set { if (_storage is IDaycareEggState egg && value != egg.IsEggAvailable) { egg.IsEggAvailable = value; _changed(); Raise(); } }
    }
    public bool HasSeed { get; }
    public string Seed
    {
        get => _storage switch { IDaycareRandomState<ushort> s => $"{s.Seed:X4}", IDaycareRandomState<uint> s => $"{s.Seed:X8}",
            IDaycareRandomState<ulong> s => $"{s.Seed:X16}", IDaycareRandomState<UInt128> s => $"{s.Seed:X32}", _ => "" };
        set
        {
            if (!HasSeed || value == Seed) return;
            if (!UInt128.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var seed)) { InvalidSeed(); return; }
            switch (_storage)
            {
                case IDaycareRandomState<ushort> s when seed <= ushort.MaxValue: s.Seed = (ushort)seed; break;
                case IDaycareRandomState<uint> s when seed <= uint.MaxValue: s.Seed = (uint)seed; break;
                case IDaycareRandomState<ulong> s when seed <= ulong.MaxValue: s.Seed = (ulong)seed; break;
                case IDaycareRandomState<UInt128> s: s.Seed = seed; break;
                default: InvalidSeed(); return;
            }
            _changed(); Raise();
        }
    }
    private void InvalidSeed() { _status("Seed inválida para esta creche."); Raise(nameof(Seed)); }
    public DaycareAreaViewModel(SaveFile sav, IDaycareStorage storage, int area, Action changed, Action<string> status,
        Action<DaycareSlotViewModel> edit, Action<DaycareSlotViewModel> deposit, Action<DaycareSlotViewModel> withdraw)
    {
        _storage = storage; _changed = changed; _status = status; Title = $"Creche {area + 1}";
        HasSeed = sav is not SAV5BW && storage is IDaycareRandomState<ushort> or IDaycareRandomState<uint> or IDaycareRandomState<ulong> or IDaycareRandomState<UInt128>;
        Slots = [.. Enumerable.Range(0, storage.DaycareSlotCount).Select(i => new DaycareSlotViewModel(sav, storage, area, i, changed, edit, deposit, withdraw))];
    }
}

public sealed class DaycareSlotViewModel : ViewModelBase
{
    public SaveFile Save { get; } public IDaycareStorage Storage { get; } public int Area { get; } public int Slot { get; }
    private readonly Action _changed;
    public PKM Pokemon => Daycares.Read(Save, Storage, Slot);
    public bool Occupied => Storage.IsDaycareOccupied(Slot);
    public string Name => Occupied ? CoreAdapter.SpeciesNames[Pokemon.Species] : "Vazio";
    public string Level => Occupied ? $"Nv. {Pokemon.CurrentLevel}" : "";
    public Bitmap? Sprite => Occupied ? SpriteService.GetSprite(Pokemon) : null;
    public bool HasExperience => Storage is IDaycareExperience;
    public decimal Experience
    {
        get => Storage is IDaycareExperience exp ? exp.GetDaycareEXP(Slot) : 0;
        set { if (Storage is IDaycareExperience exp && value != Experience) { exp.SetDaycareEXP(Slot, (uint)Math.Clamp(value, 0, uint.MaxValue)); _changed(); Raise(); } }
    }
    public RelayCommand EditCommand { get; } public RelayCommand DepositCommand { get; } public RelayCommand WithdrawCommand { get; }
    public DaycareSlotViewModel(SaveFile sav, IDaycareStorage storage, int area, int slot, Action changed,
        Action<DaycareSlotViewModel> edit, Action<DaycareSlotViewModel> deposit, Action<DaycareSlotViewModel> withdraw)
    {
        Save = sav; Storage = storage; Area = area; Slot = slot; _changed = changed;
        EditCommand = new(() => edit(this), () => Occupied);
        DepositCommand = new(() => deposit(this), () => !Occupied);
        WithdrawCommand = new(() => withdraw(this), () => Occupied);
    }
}
