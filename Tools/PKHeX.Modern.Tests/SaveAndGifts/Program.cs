using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0]; SaveBackup.Folder = Path.Combine(dir, "backups"); BankStorage.Root = Path.Combine(dir, "bank");
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Run(Task task, bool answer = true) { for (int i = 0; i < 1000 && !task.IsCompleted; i++) { Pump(); vmDialog()?.Complete(answer); System.Threading.Thread.Sleep(5); } if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); Pump(); }
MainViewModel? vm = null;
ConfirmDialogViewModel? vmDialog() => vm?.Dialog;
var sav = BlankSaveFile.Get(GameVersion.B); CoreAdapter.Activate(sav);
var gift = EncounterDatabase.LoadGifts(sav, true).OfType<DataMysteryGift>().First(g => g is PGF && g.IsEntity && EncounterDatabase.ToEntity(sav, g, out _) is not null);
var pk = EncounterDatabase.ToEntity(sav, gift, out _)!;
sav.SetBoxSlotAtIndex(pk, 0, 0);
var path = Path.Combine(dir, "black.sav"); File.WriteAllBytes(path, sav.Write().ToArray());
vm = new MainViewModel(); vm.LegalMode = false; vm.Open(path);
var win = new MainWindow { DataContext = vm, Width = 1100, Height = 720 }; win.Show();
Run(vm.SelectSlotAsync(vm.Boxes.Slots[0]));
vm.Editor!.Level = 80; Check("edição pendente", vm.Editor.IsModified);
win.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control); Pump();
Check("Ctrl+S inclui edição sem Aplicar", CoreAdapter.LoadSave(path)!.GetBoxSlotAtIndex(0, 0).CurrentLevel == 80 && !vm.Editor.IsModified && !vm.IsDirty);
vm.Editor.Level = 81;
var saveButton = win.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == "💾  Salvar");
var point = saveButton.TranslatePoint(new Avalonia.Point(saveButton.Bounds.Width / 2, saveButton.Bounds.Height / 2), win)!.Value;
win.MouseDown(point, MouseButton.Left); win.MouseUp(point, MouseButton.Left); Pump();
Check("clique em Salvar inclui edição", CoreAdapter.LoadSave(path)!.GetBoxSlotAtIndex(0, 0).CurrentLevel == 81 && !vm.IsDirty);
var before = File.ReadAllBytes(path);
vm.Editor.Level = 82;
using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
{
    Check("falha de gravação é informada", !vm.Export(path) && vm.SaveError is not null && vm.IsDirty);
    win.MouseDown(point, MouseButton.Left); win.MouseUp(point, MouseButton.Left); Pump();
    for (int w = 0; w < 50 && vm.Dialog is null; w++) Pump(); // a janela abre depois da gravacao falhar (assincrono)
    Check("clique com erro mostra janela", vm.Dialog?.Title == "Não foi possível salvar");
    vm.Dialog?.Complete(false); Pump();
}
Check("falha preserva arquivo", File.ReadAllBytes(path).SequenceEqual(before));
vm.QuickSave();
var giftPath = Path.Combine(dir, "event.pgf"); File.WriteAllBytes(giftPath, gift.Write().ToArray());
Run(vm.ImportFileAsync(vm.Boxes.Slots[1], giftPath));
Check("Mystery Gift gera Pokémon no slot", vm.Boxes.Slots[1].Pkm!.Species == gift.Species && vm.IsDirty);
vm.UndoCommand.Execute(null); Pump();
Check("importação pode ser desfeita", vm.Boxes.Slots[1].IsEmpty);
Run(vm.ImportFileAsync(vm.Boxes.Slots[0], giftPath), false);
Check("cancelar substituição mantém slot", vm.Boxes.Slots[0].Pkm!.CurrentLevel == 82);
var item = new PGF(new byte[PGF.Size]) { IsItem = true };
File.WriteAllBytes(Path.Combine(dir, "item.pgf"), item.Write().ToArray());
Run(vm.ImportFileAsync(vm.Boxes.Slots[1], Path.Combine(dir, "item.pgf")));
Check("presente de itens não entra no slot", vm.Boxes.Slots[1].IsEmpty && vm.Status.Contains("contém itens"));
var zip = Path.Combine(dir, "backup.zip");
using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntryFromFile(path, "main");
vm.Open(ZipSaves.Combine(zip, "main")); Run(vm.SelectSlotAsync(vm.Boxes.Slots[0])); vm.Editor!.Level = 83; vm.QuickSave();
Check("Salvar ZIP inclui edição", ZipSaves.Load(ZipSaves.Combine(zip, "main"))!.GetBoxSlotAtIndex(0, 0).CurrentLevel == 83);
var previousMove = vm.Editor.Move1;
vm.Editor.Move1 = 165; vm.LegalMode = true;
var zipBefore = File.ReadAllBytes(zip);
Check("edição ilegal bloqueia Salvar", !vm.Editor.CanApply && !vm.Export(ZipSaves.Combine(zip, "main")) && vm.SaveError is not null);
Check("bloqueio preserva ZIP e edição", File.ReadAllBytes(zip).SequenceEqual(zipBefore) && vm.Editor.IsModified);
vm.LegalMode = false; vm.Editor.Move1 = previousMove;
var card = EncounterDatabase.LoadGifts(sav, false).OfType<PCD>().First(g => g.IsEntity && EncounterDatabase.ToEntity(sav, g, out _) is not null);
var cardPath = Path.Combine(dir, "event.pcd"); File.WriteAllBytes(cardPath, card.Write().ToArray());
Check("PCD gera e converte para o save", CoreAdapter.LoadEntityFile(sav, cardPath) is { Format: 5 } result && result.Species == card.Species);
var wc = new WC6(new byte[WC6.Size]) { IsEntity = true, Species = 25 };
var wcPath = Path.Combine(dir, "incompatible.wc6"); File.WriteAllBytes(wcPath, wc.Write().ToArray());
Check("evento de geração posterior recusado", CoreAdapter.LoadEntityFile(sav, wcPath) is null);
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
