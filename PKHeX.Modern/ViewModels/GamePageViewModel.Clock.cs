using System;
using PKHeX.Core;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    public bool HasClock => _sav is SAV3 { SmallBlock: ISaveBlock3SmallHoenn };
    public bool IsClockTab => Tab == 7;
    public RtcClockViewModel? InitialClock { get; private set; }
    public RtcClockViewModel? ElapsedClock { get; private set; }
    public const string ClockExplanation = "O relógio da bateria do cartucho controla frutas, maré e eventos diários. Corrigir relógio parado avança o relógio decorrido para pelo menos 734 dias, como o PKHeX; não muda a hora do aparelho. Salvar grava a edição.";
    public string ClockNote => ClockExplanation;
    private void RefreshClock()
    {
        InitialClock = null; ElapsedClock = null;
        if (_sav is SAV3 { SmallBlock: ISaveBlock3SmallHoenn block })
        {
            InitialClock = new("Relógio inicial", () => block.ClockInitial, clock => block.ClockInitial = clock, Edit);
            ElapsedClock = new("Relógio decorrido", () => block.ClockElapsed, clock => block.ClockElapsed = clock, Edit);
        }
        Raise(nameof(InitialClock)); Raise(nameof(ElapsedClock)); Raise(nameof(HasClock));
    }
    public RelayCommand FixClockCommand => new(() =>
    {
        if (ElapsedClock is { } clock) clock.Day = Math.Max(734, clock.Day);
    });
    public RelayCommand AdvanceClockCommand => new(() =>
    {
        if (ElapsedClock is not { } clock) return;
        if (clock.Day == ushort.MaxValue) { _status("O relógio já atingiu o limite de dias."); return; }
        clock.Day++;
    });
}

public sealed class RtcClockViewModel(string name, Func<RTC3> get, Action<RTC3> set, Action<string, Action> edit) : ViewModelBase
{
    public string Name => name;
    public int Day { get => get().Day; set { if (value is >= 0 and <= ushort.MaxValue) Change(c => c.Day = value); } }
    public int Hour { get => get().Hour; set { if (value is >= 0 and <= 23) Change(c => c.Hour = value); } }
    public int Minute { get => get().Minute; set { if (value is >= 0 and <= 59) Change(c => c.Minute = value); } }
    public int Second { get => get().Second; set { if (value is >= 0 and <= 59) Change(c => c.Second = value); } }
    private void Change(Action<RTC3> change)
    {
        edit(name, () => { var clock = get(); change(clock); set(clock); }); Raise(string.Empty);
    }
}
