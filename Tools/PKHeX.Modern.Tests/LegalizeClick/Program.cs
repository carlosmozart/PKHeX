using System; using System.Diagnostics; using System.IO; using System.Linq; using Avalonia; using Avalonia.Headless; using Avalonia.Threading;
using PKHeX.Core; using PKHeX.Modern; using PKHeX.Modern.Services; using PKHeX.Modern.ViewModels; using PKHeX.Modern.Views;
// Clicar num Pokemon ilegal pergunta se quer legalizar: "Legalizar" grava legal no slot (com Ctrl+Z); "So abrir" nao pergunta de novo.
// Save sintetico (Black) com um Pikachu legal e uma copia estragada.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(dir, "bank"); SaveBackup.Folder = Path.Combine(dir, "backups");
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {n} {extra}"); if (!ok) fails++; }
var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "TESTE";
CoreAdapter.Activate(sav);
var pk = EncounterDatabase.SearchEncounters(sav, 25, true).Where(e => e.Species == 25).Select(e => EncounterDatabase.ToEntity(sav, e, out _)).First(p => p is not null && new LegalityAnalysis(p).Valid)!;
var bad = pk.Clone(); bad.MetLevel = 0; bad.MetLocation = 60000; bad.RefreshChecksum();
Check("amostra: Pikachu estragado é ilegal", !new LegalityAnalysis(bad).Valid);
sav.SetBoxSlotAtIndex(bad, 0, 0); sav.SetBoxSlotAtIndex(bad.Clone(), 0, 1); sav.SetBoxSlotAtIndex(bad.Clone(), 0, 2);
var path = Path.Combine(dir, "legalize.sav"); File.WriteAllBytes(path, sav.Write().ToArray());

var vm = new MainViewModel(new AppSettings { CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
string? asked = null; bool answer = true;
void Wait(Func<bool> done, int ms = 60000) { var sw = Stopwatch.StartNew(); while (!done() && sw.ElapsedMilliseconds < ms) { Pump(); if (vm.Dialog is { } d) { asked = d.Title; d.Complete(answer); } System.Threading.Thread.Sleep(10); } Pump(); }
var open = vm.OpenAsync(path); Wait(() => open.IsCompleted);
vm.CurrentPage = vm.Boxes; Pump();

// "Legalizar": grava legal no slot e Ctrl+Z volta.
asked = null; answer = true;
var t = vm.SelectSlotAsync(vm.Boxes.Slots[0]); Wait(() => t.IsCompleted && vm.Editor is { IsLegalizing: false });
Check("pergunta ao clicar num Pokémon ilegal", asked == "Pokémon ilegal", asked ?? "-");
Check("Legalizar grava um Pokémon legal no slot", vm.Boxes.Slots[0].IsLegal == true && vm.IsDirty);
vm.UndoCommand.Execute(null); Pump();
Check("Ctrl+Z volta o Pokémon ilegal", vm.Boxes.Slots[0].IsLegal == false);

// "So abrir": abre sem mudar e nao pergunta de novo nesta sessao.
asked = null; answer = false;
t = vm.SelectSlotAsync(vm.Boxes.Slots[1]); Wait(() => t.IsCompleted);
Check("Só abrir não muda o slot", asked == "Pokémon ilegal" && vm.Boxes.Slots[1].IsLegal == false && vm.Editor?.ShowIllegal == true);
t = vm.SelectSlotAsync(vm.Boxes.Slots[0]); Wait(() => t.IsCompleted && vm.Editor is { IsLegalizing: false });
asked = null;
t = vm.SelectSlotAsync(vm.Boxes.Slots[1]); Wait(() => t.IsCompleted);
Check("depois de Só abrir, não pergunta de novo", asked is null, asked ?? "-");

// Preferencia desligada: so abre no editor, sem pergunta.
vm.AskLegalizeOnClick = false; asked = null;
t = vm.SelectSlotAsync(vm.Boxes.Slots[2]); Wait(() => t.IsCompleted);
Check("opção desligada não pergunta", asked is null && vm.Editor?.ShowIllegal == true && vm.Boxes.Slots[2].IsLegal == false, asked ?? "-");
vm.AskLegalizeOnClick = true;

// Pokemon legal: nenhuma pergunta.
var good = vm.Boxes.Slots.First(s => s.IsLegal == true || s.IsEmpty);
asked = null;
if (!good.IsEmpty) { t = vm.SelectSlotAsync(good); Wait(() => t.IsCompleted); }
Check("Pokémon legal não pergunta", asked is null, asked ?? "-");

win.Close();
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
