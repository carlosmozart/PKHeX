using System; using System.IO; using System.Linq; using System.Threading.Tasks; using Avalonia; using Avalonia.Headless; using Avalonia.Threading; using Avalonia.Input;
using PKHeX.Core; using PKHeX.Modern; using PKHeX.Modern.Services; using PKHeX.Modern.ViewModels; using PKHeX.Modern.Views;
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0]; int fails = 0;
SaveBackup.Folder = Path.Combine(dir, "backups");
BankStorage.Root = Path.Combine(dir, "bank");
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {n} {extra}"); if (!ok) fails++; }
string F(string n) => Path.Combine(dir, n);
// Garante um Pokemon no slot 1 da caixa 1 do FireRed (a copia do save pode ter as caixas vazias): copia o 1o da equipe.
{
    var f = CoreAdapter.LoadSave(F("fr.sav"))!;
    if (f.GetBoxSlotAtIndex(0, 0).Species == 0)
    {
        f.SetBoxSlotAtIndex(f.GetPartySlotAtIndex(0), 0, 0);
        File.WriteAllBytes(F("fr.sav"), f.Write().ToArray());
    }
}
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
        System.Threading.Thread.Sleep(5);
    }
    if (!t.IsCompleted) throw new TimeoutException();
    t.GetAwaiter().GetResult(); Pump();
}
void Shot(string n) { Pump(); win.CaptureRenderedFrame()!.Save(F(n)); }
string Box0() => string.Join(",", vm.Boxes.Slots.Take(3).Select(s => s.IsEmpty ? "-" : s.Title));

Run(() => vm.OpenAsync(F("fr.sav")));
var frBox = Box0();
Run(() => vm.OpenAsync(F("hg.sav")));
Run(() => vm.OpenAsync(F("em.sav")));
Check("3 abas, Emerald ativa", vm.OpenSaves.Count == 3 && vm.ActiveTab!.FileName == "em.sav", string.Join(" | ", vm.OpenSaves.Select(t => t.FileName + (t.IsActive ? "*" : ""))));

// volta para o FireRed, muda de caixa e move um Pokemon (fica com alteracao)
Run(() => vm.SwitchToAsync(vm.OpenSaves[0]));
Check("FireRed ativo com as caixas dele", vm.ActiveTab!.FileName == "fr.sav" && Box0() == frBox, Box0());
vm.MoveSlot(vm.Boxes.Slots[0], vm.Boxes.Slots[1], false);
var moved = Box0();
vm.Boxes.CurrentBox = 3;
Check("FireRed com alteracao", vm.IsDirty && vm.OpenSaves[0].IsDirty && vm.UndoCommand.CanExecute(null));
Shot("tabs_fr.png");

// troca para o HeartGold: estado limpo
Run(() => vm.SwitchToAsync(vm.OpenSaves[1]));
Check("HeartGold limpo", !vm.IsDirty && !vm.UndoCommand.CanExecute(null) && vm.Boxes.CurrentBox == 0, vm.GameName);
Check("FireRed continua marcado com ●", vm.OpenSaves[0].IsDirty);

// Ctrl+Tab volta ao Emerald, Ctrl+Shift+Tab volta ao HeartGold
win.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control); Pump(); Run(() => Task.Delay(50));
Check("Ctrl+Tab", vm.ActiveTab!.FileName == "em.sav", vm.ActiveTab.FileName);
win.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift); Pump(); Run(() => Task.Delay(50));
Check("Ctrl+Shift+Tab", vm.ActiveTab!.FileName == "hg.sav", vm.ActiveTab.FileName);

// de volta ao FireRed: alteracao, desfazer e caixa preservados
Run(() => vm.SwitchToAsync(vm.OpenSaves[0]));
Check("FireRed restaurado (caixa 4, desfazer)", vm.IsDirty && vm.Boxes.CurrentBox == 3 && vm.UndoCommand.CanExecute(null), $"caixa {vm.Boxes.CurrentBox + 1}");
vm.Boxes.CurrentBox = 0;
Check("movimento continua la", Box0() == moved, Box0());
vm.UndoCommand.Execute(null); Pump();
Check("desfazer funciona na aba", Box0() == frBox, Box0());
vm.RedoCommand.Execute(null); Pump();

// abrir de novo o mesmo arquivo so ativa a aba
Run(() => vm.OpenAsync(F("em.sav")));
Check("reabrir ativa a aba existente", vm.OpenSaves.Count == 3 && vm.ActiveTab!.FileName == "em.sav", vm.Status);

// fechar o app com o FireRed sujo pergunta (cancelar)
bool closeOk = true;
Run(async () => closeOk = await vm.ConfirmCloseAsync(), false);
Check("fechar app pergunta e cancela", !closeOk);

// fechar abas: Emerald (limpa, sem pergunta), FireRed (suja: cancelar, depois confirmar)
Run(() => vm.CloseTabAsync(vm.ActiveTab!));
Check("Emerald fechado, outra aba ativa", vm.OpenSaves.Count == 2 && vm.HasSave, vm.ActiveTab?.FileName);
var frTab = vm.OpenSaves.First(t => t.FileName == "fr.sav");
Run(() => vm.CloseTabAsync(frTab), false);
Check("cancelar mantem a aba suja", vm.OpenSaves.Contains(frTab));
Run(() => vm.CloseTabAsync(frTab), true);
Check("confirmar fecha", !vm.OpenSaves.Contains(frTab) && vm.OpenSaves.Count == 1);
Run(() => vm.CloseTabAsync(vm.OpenSaves[0]));
Check("ultima aba fechada -> tela inicial", vm.OpenSaves.Count == 0 && !vm.HasSave && vm.ShowHomeSaves);

// recarregar do disco (restaurar backup)
Run(() => vm.OpenAsync(F("fr.sav")));
vm.MoveSlot(vm.Boxes.Slots[0], vm.Boxes.Slots[1], false);
Run(() => vm.ReloadAsync(F("fr.sav")), true);
Check("recarregar descarta e rele do disco", !vm.IsDirty && Box0() == frBox && vm.OpenSaves.Count == 1, vm.Status);
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
