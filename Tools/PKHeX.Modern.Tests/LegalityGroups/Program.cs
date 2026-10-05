using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
void Check(string n, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {n}"); if (!ok) fails++; }
var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "DEMO"; CoreAdapter.Activate(sav);
var pk = EncounterDatabase.SearchEncounters(sav, 504, true).Where(e => e.Species == 504)
    .Select(e => EncounterDatabase.ToEntity(sav, e, out _)).First(p => p is not null && new LegalityAnalysis(p).Valid)!;
pk.Move1 = 165; pk.Ball = 26;
var editor = new PokemonEditorViewModel(pk, "Caixa 1 · 1", _ => { }, _ => { }, sav: sav, legalMode: true);
foreach (var group in editor.LegalityGroups) Console.WriteLine(group.Topic + ": " + string.Join(" / ", group.Issues));
Check("golpes e bola agrupados", editor.LegalityGroups.Any(g => g.Topic == "Golpes") && editor.LegalityGroups.Any(g => g.Topic == "Bola"));
Check("primeiro inválido aberto", editor.LegalityGroups.First(g => g.Invalid).Expanded);
Check("restante recolhido", editor.LegalityGroups.Skip(1).All(g => !g.Expanded));
Check("ordem de gravidade", !editor.LegalityGroups.SkipWhile(g => g.Invalid).Any(g => g.Invalid));
Check("sem botão falso", editor.LegalityGroups.Where(g => g.Topic is not ("Golpes" or "Bola" or "Fitas e marcas" or "Encontro")).All(g => !g.HasAction));
sav.SetBoxSlotAtIndex(pk, 0, 0); var path = Path.Combine(work, "Black.sav"); File.WriteAllBytes(path, sav.Write().ToArray());
var vm = new MainViewModel(new AppSettings { CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 950 }; win.Show(); vm.Open(path); vm.CurrentPage = vm.Boxes;
var select = vm.SelectSlotAsync(vm.Boxes.Slots[0]);
var sw = System.Diagnostics.Stopwatch.StartNew();
while (!select.IsCompleted && sw.ElapsedMilliseconds < 60000) { Dispatcher.UIThread.RunJobs(); vm.Dialog?.Complete(false); System.Threading.Thread.Sleep(10); }
win.UpdateLayout(); for (int i = 0; i < 12; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
win.CaptureRenderedFrame()?.Save(Path.Combine(work, "legality-groups.png"));
editor.LegalityGroups.First(g => g.Topic == "Golpes").Action!.Execute(null);
Check("correção de golpes limpa grupo", editor.LegalityGroups.All(g => g.Topic != "Golpes"));
editor.LegalityGroups.First(g => g.Topic == "Bola").Action!.Execute(null);
Check("bola legal limpa grupo", editor.LegalityGroups.All(g => g.Topic != "Bola"));
Check("fonte intacta", pk.Move1 == 165 && pk.Ball == 26);
Check("mapeamento completo", GroupedLegality.Topic(CheckIdentifier.RibbonMark) == "Fitas e marcas" && GroupedLegality.Topic(CheckIdentifier.Memory) == "Memórias" && GroupedLegality.Topic(CheckIdentifier.Level) == "Nível e experiência");
win.Close(); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
