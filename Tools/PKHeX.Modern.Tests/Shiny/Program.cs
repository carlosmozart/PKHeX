using System; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Diagnostics; using Avalonia; using Avalonia.Headless; using Avalonia.Threading; using Avalonia.Input; using Avalonia.VisualTree; using Avalonia.Controls;
using PKHeX.Core; using PKHeX.Modern; using PKHeX.Modern.Services; using PKHeX.Modern.ViewModels; using PKHeX.Modern.Views;
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(dir, "bank");
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {n} {extra}"); if (!ok) fails++; }
SaveBackup.Folder = Path.Combine(dir, "backups");
var vm = new MainViewModel();
var win = new MainWindow { DataContext = vm, Width = 1500, Height = 900 }; win.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Wait(Func<bool> done, int ms = 60000) { var sw = Stopwatch.StartNew(); while (!done() && sw.ElapsedMilliseconds < ms) { Pump(); if (vm.Dialog is { } d) d.Complete(true); System.Threading.Thread.Sleep(10); } Pump(); }
SaveFile Sav() => (SaveFile)typeof(MainViewModel).GetField("_sav", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
void ClickStar(RawInputModifiers mods)
{
    var star = win.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ShinyStar");
    var p = star.TranslatePoint(new Point(star.Bounds.Width / 2, star.Bounds.Height / 2), win)!.Value;
    win.MouseDown(p, MouseButton.Left, mods); win.MouseUp(p, MouseButton.Left, mods); Pump();
    Wait(() => !vm.Editor!.IsLegalizing);
}
void Open(string f, Func<StoredEntity, LegalityAnalysis, bool> pick)
{
    var t = vm.OpenAsync(Path.Combine(dir, f)); Wait(() => t.IsCompleted);
    var e = EntitySearch.ReadAll(Sav()).Where(x => x.Box >= 0 && x.Pkm.Species != 0 && !x.Pkm.IsEgg && !x.Pkm.IsShiny).First(x => pick(x, new LegalityAnalysis(x.Pkm)));
    vm.CurrentPage = vm.Boxes; vm.Boxes.CurrentBox = e.Box; Pump();
    var s = vm.SelectSlotAsync(vm.Boxes.Slots[e.Slot]); Wait(() => s.IsCompleted);
    Console.WriteLine($"-- {f}: {vm.Editor!.SpeciesName} (PID {vm.Editor.PID}, SID {Sav().GetBoxSlotAtIndex(e.Box, e.Slot).SID16})");
}
ShinySummary S() { var ed = vm.Editor!; return new ShinySummary(ed.ShinySymbol, ed.IsShiny, ed.IsLegal, ed.PID); }

// Gen 3: selvagem (PID/IV ligados)
Open("s.sav", (e, la) => la.Valid && la.EncounterMatch is IEncounterSlot3);
var pid0 = vm.Editor!.PID;
ClickStar(RawInputModifiers.Alt);
var a = S(); Check("Gen3 Alt+clique: shiny, mesmo PID, legal", a.Shiny && a.Pid == pid0 && a.Legal, $"{a} | {vm.Status}");
ClickStar(RawInputModifiers.None);
var b = S(); Check("Gen3 clique em shiny: tira o shiny", !b.Shiny && b.Legal, $"{b} | {vm.Status}");
ClickStar(RawInputModifiers.None);
var c = S(); Check("Gen3 clique: shiny legal (PID novo)", c.Shiny && c.Legal, $"{c} | {vm.Status}");

// Gen 8: quadrado e estrela
Open("sh.sav", (e, la) => la.Valid && !la.Info.EncounterMatch.Shiny.IsValid(new PK8()) is var _ && la.EncounterMatch is not IFixedTrainer && la.EncounterMatch.Shiny == Shiny.Random);
ClickStar(RawInputModifiers.Shift);
var d = S(); Check("Gen8 Shift+clique: quadrado ◆", d.Shiny && d.Symbol == "◆" && d.Legal, $"{d} | {vm.Status}");
ClickStar(RawInputModifiers.None);
ClickStar(RawInputModifiers.Control);
var f = S(); Check("Gen8 Ctrl+clique: estrela ★", f.Shiny && f.Symbol == "★" && f.Legal, $"{f} | {vm.Status}");
win.CaptureRenderedFrame()!.Save(Path.Combine(dir, "star.png"));
ClickStar(RawInputModifiers.None);
win.CaptureRenderedFrame()!.Save(Path.Combine(dir, "star_off.png"));
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
record ShinySummary(string Symbol, bool Shiny, bool Legal, string Pid);
