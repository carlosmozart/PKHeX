using System; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Diagnostics; using Avalonia; using Avalonia.Headless; using Avalonia.Threading;
using PKHeX.Core; using PKHeX.Modern; using PKHeX.Modern.Services; using PKHeX.Modern.ViewModels; using PKHeX.Modern.Views;
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0]; int fails = 0;
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {n} {extra}"); if (!ok) fails++; }
SaveBackup.Folder = Path.Combine(dir, "backups");
BankStorage.Root = Path.Combine(dir, "bank");
var vm = new MainViewModel();
var win = new MainWindow { DataContext = vm, Width = 1500, Height = 950 }; win.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
bool Wait(Func<bool> done, int ms, bool answer = true)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < ms && !done())
    {
        Pump();
        if (vm.Dialog is { } d) { Console.WriteLine($"     [pergunta] {d.Title}: {d.Message} -> {(answer ? d.ConfirmText : d.CancelText)}"); d.Complete(answer); }
        System.Threading.Thread.Sleep(10);
    }
    return done();
}
vm.Open(Path.Combine(dir, "y.sav")); Pump();
Console.WriteLine($"Aberto: {vm.GameName}, legal mode {vm.LegalMode}");
// todos os ilegais
var all = EntitySearch.ReadAll((SaveFile)typeof(MainViewModel).GetField("_sav", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(vm)!);
foreach (var e in all.Where(e => e.Pkm.Species != 0 && CoreAdapter.IsLegal(e.Pkm) == false))
    Console.WriteLine($"  ilegal: {CoreAdapter.SpeciesNames[e.Pkm.Species]} caixa {e.Box + 1} slot {e.Slot + 1}: {string.Join(" / ", CoreAdapter.GetLegalityIssues(e.Pkm, 2))}");
var g = all.First(e => e.Pkm.Species == 94);
Console.WriteLine($"Gengar em caixa {g.Box + 1} slot {g.Slot + 1}");
SlotViewModel slot;
if (g.Box < 0) { vm.CurrentPage = vm.Party; Pump(); slot = vm.Party.Slots[g.Slot]; }
else { vm.Boxes.CurrentBox = g.Box; Pump(); slot = vm.Boxes.Slots[g.Slot]; }
vm.SelectSlotAsync(slot); Wait(() => vm.Editor?.SpeciesName == "Gengar", 3000);
var ed = vm.Editor!;
Console.WriteLine($"Editor: {ed.SpeciesName} legal={ed.IsLegal} showLegalize={ed.ShowLegalize}");
var sw = Stopwatch.StartNew();
ed.LegalizeCommand.Execute(null);
var finished = Wait(() => !ed.IsLegalizing, 60000);
Console.WriteLine($"Legalizar terminou={finished} em {sw.ElapsedMilliseconds} ms; legal={ed.IsLegal}; status: {vm.Status}");
// tentar outro pokemon
var other = (g.Box < 0 ? vm.Party.Slots : vm.Boxes.Slots).First(s => !s.IsEmpty && s != slot);
vm.SelectSlotAsync(other);
var switched = Wait(() => vm.Editor != ed, 5000, answer: true);
Console.WriteLine($"Trocou para outro: {switched} editor={vm.Editor?.SpeciesName} status: {vm.Status}");
Check("Gengar legalizado (Gen 1, sem travar)", finished && ed.IsLegal, vm.Status);
Check("editor troca para outro Pokemon depois", switched);
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
