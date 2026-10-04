using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups"); int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
foreach (var file in new[] { "em.sav", "sa.sav" })
{
    var sav = CoreAdapter.LoadSave(Path.Combine(work, file))!; var page = new GamePageViewModel((_, _, _) => Task.FromResult(true), Console.WriteLine); page.Load(sav); page.Tab = 7;
    Check(file + " oferece relógio", page.HasClock && page.IsClockTab);
    page.InitialClock!.Day = 21; page.InitialClock.Hour = 13; page.InitialClock.Minute = 14; page.InitialClock.Second = 15;
    page.ElapsedClock!.Day = 10; page.ElapsedClock.Hour = 4; page.ElapsedClock.Minute = 5; page.ElapsedClock.Second = 6;
    var before = sav.Write().ToArray(); int count = page.History!.Count;
    page.FixClockCommand.Execute(null); Check(file + " corrige decorrido conforme PKHeX", page.ElapsedClock.Day == 734 && page.InitialClock.Day == 21 && page.ElapsedClock.Hour == 4 && page.History.Count == count + 1);
    page.UndoGame(); Check(file + " desfaz correção byte a byte", sav.Write().Span.SequenceEqual(before)); page.RedoGame();
    page.AdvanceClockCommand.Execute(null); Check(file + " avança um dia", page.ElapsedClock!.Day == 735);
    var path = Path.Combine(work, "out-" + file); File.WriteAllBytes(path, sav.Write().ToArray()); var read = (SAV3)CoreAdapter.LoadSave(path)!; var clocks = (ISaveBlock3SmallHoenn)read.SmallBlock;
    Check(file + " reabre campos e checksum", read.ChecksumsValid && clocks.ClockInitial.Day == 21 && clocks.ClockInitial.Hour == 13 && clocks.ClockInitial.Minute == 14 && clocks.ClockInitial.Second == 15 && clocks.ClockElapsed.Day == 735 && clocks.ClockElapsed.Hour == 4 && clocks.ClockElapsed.Minute == 5 && clocks.ClockElapsed.Second == 6);
    page.ElapsedClock.Day = 65535; page.AdvanceClockCommand.Execute(null); Check(file + " não transborda", page.ElapsedClock.Day == 65535);
}
var demo = BlankSaveFile.Get(GameVersion.E); var vm = new GamePageViewModel((_, _, _) => Task.FromResult(true), _ => { }); vm.Load(demo); vm.Tab = 7; vm.InitialClock!.Hour = 8; vm.ElapsedClock!.Day = 734;
var win = new Window { Content = new ContentControl { Content = vm, Margin = new Thickness(16) }, Width = 948, Height = 620 }; win.Show();
for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } win.CaptureRenderedFrame()!.Save(Path.Combine(work, "clock.png")); win.Close();
vm.Load(BlankSaveFile.Get(GameVersion.FR)); Check("FireRed esconde relógio", !vm.HasClock); vm.Load(BlankSaveFile.Get(GameVersion.B)); Check("outros jogos escondem relógio", !vm.HasClock);
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
