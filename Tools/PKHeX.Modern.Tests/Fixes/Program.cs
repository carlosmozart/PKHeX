using System; using System.IO; using System.Linq; using System.Diagnostics; using Avalonia; using Avalonia.Headless; using Avalonia.Threading; using Avalonia.Controls; using Avalonia.VisualTree;
using PKHeX.Core; using PKHeX.Modern; using PKHeX.Modern.Services; using PKHeX.Modern.ViewModels; using PKHeX.Modern.Views;
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0]; int fails = 0;
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {n} {extra}"); if (!ok) fails++; }
SaveBackup.Folder = Path.Combine(dir, "backups"); BankStorage.Root = Path.Combine(dir, "bank");
var vm = new MainViewModel();
var win = new MainWindow { DataContext = vm, Width = 1500, Height = 1000 }; win.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Wait(Func<bool> done, int ms = 60000) { var sw = Stopwatch.StartNew(); while (!done() && sw.ElapsedMilliseconds < ms) { Pump(); if (vm.Dialog is { } d) d.Complete(true); System.Threading.Thread.Sleep(10); } Pump(); }
SaveFile Sav() => (SaveFile)typeof(MainViewModel).GetField("_sav", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
void Open(string f) { var t = vm.OpenAsync(Path.Combine(dir, f)); Wait(() => t.IsCompleted); }
void Select(StoredEntity e)
{
    SlotViewModel slot;
    if (e.Box < 0) { vm.CurrentPage = vm.Party; Pump(); slot = vm.Party.Slots[e.Slot]; }
    else { vm.CurrentPage = vm.Boxes; vm.Boxes.CurrentBox = e.Box; Pump(); slot = vm.Boxes.Slots[e.Slot]; }
    var s = vm.SelectSlotAsync(slot); Wait(() => s.IsCompleted);
    vm.Editor!.SelectedTab = 0; Pump();
}
void Run(System.Windows.Input.ICommand c) { c.Execute(null); Pump(); Wait(() => !vm.Editor!.IsLegalizing); }
string Issues() => string.Join(" | ", vm.Editor!.LegalityIssues);
vm.LegalMode = true;

// 1) Whismur (Sapphire, equipe): legal com 2 avisos Fishy
Open("s.sav");
var w = EntitySearch.ReadAll(Sav()).First(x => x.Pkm.Species == (ushort)Species.Whismur);
Select(w);
var ed = vm.Editor!;
var pid0 = ed.PID;
Check("Whismur abre com avisos", ed.HasWarnings && ed.ShowLegalize, Issues());
Run(ed.LegalizeCommand); ed = vm.Editor!;
Check("Legalizar tira os avisos", ed.IsLegal && !ed.HasWarnings && ed.LegalityIssues.Count == 0, $"{Issues()} | {vm.Status}");
Check("Legalizar manteve o PID (sem trocar o encontro)", ed.PID == pid0, ed.PID + " vs " + pid0);
win.CaptureRenderedFrame()!.Save(Path.Combine(dir, "whismur_after.png"));
// 2) Botoes de correcao num Pokemon com golpes ilegais (modo legal desligado para quebrar)
vm.LegalMode = false; Pump();
var target = EntitySearch.ReadAll(Sav()).Where(x => x.Box >= 0 && !x.Pkm.IsEgg && x.Pkm.Species != 0).First(x => new LegalityAnalysis(x.Pkm).Valid && x.Pkm.CurrentLevel > 10);
Select(target); ed = vm.Editor!;
Console.WriteLine($"-- alvo: {ed.SpeciesName} Lv{ed.Level}");
ed.Move1 = 94; Pump(); // Psychic: ilegal para quase todos
Check("golpe ilegal quebra", !ed.IsLegal, Issues());
win.CaptureRenderedFrame()!.Save(Path.Combine(dir, "fix_before.png"));
Run(ed.SuggestMovesCommand);
Check("Golpes sugeridos conserta", ed.IsLegal, $"{Issues()} | {vm.Status}");
var lvl = ed.Level;
ed.MetLevel = Math.Min(100, lvl + 5); Pump();
Check("nivel de encontro acima do atual quebra", !ed.IsLegal, Issues());
Run(ed.SuggestMetCommand);
Check("Encontro sugerido conserta", ed.IsLegal, $"{Issues()} | {vm.Status}");
// IVs maximos fora do modo legal: aplica (Gen 3 selvagem fica ilegal, esperado)
Run(ed.MaxIVsCommand);
Check("IVs máximos aplica (modo legal desligado)", ed.Stats.All(x => x.IV == 31), vm.Status);
// Modo legal: golpe ilegal desfeito; Golpes sugeridos quando ja esta legal nao quebra
vm.LegalMode = true; Pump();
Select(target); ed = vm.Editor!;
Run(ed.SuggestMovesCommand);
Check("Modo legal: Golpes sugeridos mantem legal", ed.IsLegal, vm.Status);
Run(ed.MaxIVsCommand); // selvagem Gen 3 -> pergunta ovo (dialogo respondido sim) ou aplica
Check("Modo legal: IVs máximos termina legal", ed.IsLegal, vm.Status);
Console.WriteLine("   status: " + vm.Status);
win.CaptureRenderedFrame()!.Save(Path.Combine(dir, "fix_after.png"));

// 3) Gen 8: golpes de reaprender
vm.LegalMode = false; Pump();
Open("sh0.sav");
var t8 = EntitySearch.ReadAll(Sav()).Where(x => x.Box >= 0 && !x.Pkm.IsEgg && x.Pkm.Species != 0).First(x => new LegalityAnalysis(x.Pkm).Valid && x.Pkm is PK8 p && p.RelearnMove1 != 0 && new LegalityAnalysis(x.Pkm).EncounterMatch is IEncounterEgg);
Select(t8); ed = vm.Editor!;
Console.WriteLine($"-- alvo Gen 8: {ed.SpeciesName}");
var pk8 = (PK8)typeof(PokemonEditorViewModel).GetField("_pk", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ed)!;
pk8.RelearnMove1 = 0; pk8.RelearnMove2 = 0; pk8.RelearnMove3 = 0; pk8.RelearnMove4 = 0; ed.Level = ed.Level; Pump();
Check("reaprender vazio quebra", !ed.IsLegal, Issues());
Run(ed.SuggestRelearnCommand); ed = vm.Editor!;
Check("Golpes de reaprender conserta", ed.IsLegal, $"{Issues()} | {vm.Status}");
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
