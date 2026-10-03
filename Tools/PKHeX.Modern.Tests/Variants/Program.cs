using System; using System.IO; using System.Linq; using System.Threading.Tasks; using Avalonia; using Avalonia.Headless; using Avalonia.Threading;
using PKHeX.Core; using PKHeX.Modern; using PKHeX.Modern.Services; using PKHeX.Modern.ViewModels; using PKHeX.Modern.Views;
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0]; int fails = 0;
SaveBackup.Folder = Path.Combine(dir, "backups");
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {n} {extra}"); if (!ok) fails++; }
BankStorage.Root = Path.Combine(dir, "bank");
Directory.CreateDirectory(BankStorage.Root);

// saves: Charizard da Gen 3 (FireRed) e um save de Black em arquivo
var fr = CoreAdapter.LoadSave(Path.Combine(dir, "fr.sav"))!;
// Charizard se houver; senao o 1o Pokemon da equipe (a copia do save pode ter as caixas vazias)
var zard = fr.BoxData.FirstOrDefault(p => p.Species == 6) ?? fr.PartyData.First(p => p.Species != 0 && !p.IsEgg);
var zname = CoreAdapter.SpeciesNames[zard.Species];
var blackPath = Path.Combine(dir, "black.sav");
File.WriteAllBytes(blackPath, BlankSaveFile.Get(GameVersion.B).Write().ToArray());

BankStorage.CreateBank("Teste"); var bankName = "Teste";
var box = BankStorage.GetBoxes(bankName)[0];
BankStorage.WriteSlot(box, 0, zard);

var vm = new MainViewModel();
var win = new MainWindow { DataContext = vm, Width = 1500, Height = 950 }; win.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Run(Func<Task> f, params bool[] answers)
{
    var t = f(); int k = 0;
    for (int n = 0; n < 1000 && !t.IsCompleted; n++)
    {
        Pump();
        if (vm.Dialog is { } d) { var a = k < answers.Length ? answers[k] : true; k++; Console.WriteLine($"     [pergunta] {d.Title} -> {(a ? d.ConfirmText : d.CancelText)}"); d.Complete(a); }
        System.Threading.Thread.Sleep(10);
    }
    if (!t.IsCompleted) throw new TimeoutException("tarefa nao terminou");
    t.GetAwaiter().GetResult(); Pump();
}
void Shot(string n) { Pump(); win.CaptureRenderedFrame()!.Save(Path.Combine(dir, n)); }

var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
SaveFile Sav() => (SaveFile)typeof(MainViewModel).GetField("_sav", flags)!.GetValue(vm)!;
Task SyncAttached() => (Task)typeof(MainViewModel).GetMethod("SyncAttachedAsync", flags)!.Invoke(vm, null)!;
{
    vm.Open(blackPath);
    vm.Bank.Reload();
    vm.Bank.SelectedBank = bankName;
    vm.Bank.AttachMode = true;
    vm.CurrentPage = vm.Bank; Pump();
    var bankSlot = vm.Bank.Slots[0];
    Check("bank tem o Pokemon da Gen 3", bankSlot.Title == zname, bankSlot.Pkm?.Extension);

    // 1. levar anexado para o Black
    Run(() => vm.MoveSlotAsync(bankSlot, vm.Boxes.Slots[0], DropMode.Move));
    var copy = vm.Boxes.Slots[0];
    Check("copia anexada no Black", copy.Title == zname && copy.Pkm!.Format == 5, $"{copy.Pkm?.Extension} status: {vm.Status}");
    Check("original continua no bank", vm.Bank.Slots[0].Title == zname && vm.Bank.Slots[0].IsAttached);

    // 2. "jogar": a copia sobe de nivel no save
    var played = copy.Pkm!.Clone(); played.CurrentLevel = (byte)(played.CurrentLevel + 5);
    CoreAdapter.SetBoxSlot(Sav(), played, 0, 0);
    int lvl = played.CurrentLevel;

    // 3. atualizar anexados -> variante PK5
    Run(() => SyncAttached());
    vm.CurrentPage = vm.Bank; Pump();
    Run(() => vm.SelectSlotAsync(vm.Bank.Slots[0]));
    Check("painel de variantes aparece", vm.Bank.HasVariants, vm.Bank.VariantsTitle);
    var v = vm.Bank.Variants.FirstOrDefault();
    Check("variante PK5 com o nivel do jogo", v is not null && v.Format == "PK5" && v.Variant.Pk.CurrentLevel == lvl, v is null ? "" : $"{v.Format} {v.Title}");
    Check("original do bank nao mudou", vm.Bank.Slots[0].Pkm!.Format == 3 && vm.Bank.Slots[0].Pkm!.CurrentLevel == zard.CurrentLevel);
    Shot("variants_panel.png");

    // 4. abrir no editor do save -> substitui a copia; aplicar religa
    Run(() => { v!.UseCommand.Execute(null); return Task.Delay(50); });
    Check("editor aberto com a variante", vm.Editor is not null && vm.Editor.Level == lvl, vm.Status);
    vm.Editor!.Level = lvl + 1;
    vm.Editor.ApplyCommand.Execute(null); Pump();
    var after = Sav().GetBoxSlotAtIndex(0, 0);
    Check("aplicado no lugar da copia", after.Species == zard.Species && after.CurrentLevel == lvl + 1, vm.Status);
    var link = BankLinks.All.FirstOrDefault();
    Check("anexado aponta para o Black", link is not null && link.SavePath is not null && Path.GetFullPath(link.SavePath) == Path.GetFullPath(blackPath), link?.SaveName);

    // 5. excluir variante
    vm.CurrentPage = vm.Bank; Pump();
    Run(() => vm.SelectSlotAsync(vm.Bank.Slots[0]));
    var v2 = vm.Bank.Variants.First();
    Run(() => { v2.DeleteCommand.Execute(null); return Task.Delay(200); }, true);
    Check("variante excluida", !vm.Bank.HasVariants && BankLinks.GetVariants(vm.Bank.Slots[0].Pkm!).Count == 0, vm.Status);
    Check("original intacto", vm.Bank.Slots[0].Pkm!.Format == 3);
}
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
