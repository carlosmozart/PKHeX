using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

// Public documentation captures use generated saves only; no personal save or desktop capture.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var work = Path.Combine(Path.GetTempPath(), "pkhex-docs-" + Guid.NewGuid()); Directory.CreateDirectory(work);
var library = Path.Combine(work, "saves"); Directory.CreateDirectory(library);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
CoreAdapter.SetLanguage("en");
PKM Make(SaveFile sav, ushort species)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, species, true).Where(e => e.Species == species))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk && new LegalityAnalysis(pk).Valid) return pk;
    throw new Exception("Sem encontro legal: " + species);
}
var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "Demo"; sav.Language = 2;
sav.SecondsToStart = (uint)(new DateTime(2012, 5, 14) - new DateTime(2000, 1, 1)).TotalSeconds;
CoreAdapter.Activate(sav);
ushort[] species = [133, 25, 175, 447, 42, 113, 1, 4, 7, 129, 147, 143, 172, 54, 37];
for (int i = 0; i < species.Length; i++)
{
    var pk = Make(sav, species[i]); sav.SetBoxSlotAtIndex(pk, 0, i);
    if (i < 6) sav.SetPartySlotAtIndex(pk.Clone(), i);
}
((IBoxDetailName)sav).SetBoxName(0, "Coleção");
((IBoxDetailWallpaper)sav).SetBoxWallpaper(0, 0);
var black = Path.Combine(library, "Black.sav"); File.WriteAllBytes(black, sav.Write().ToArray());
foreach (var version in new[] { GameVersion.BD, GameVersion.SP })
{
    var demo = BlankSaveFile.Get(version); demo.OT = "Demo"; demo.Language = 2;
    File.WriteAllBytes(Path.Combine(library, version + ".sav"), demo.Write().ToArray());
}
using (var zip = ZipFile.Open(Path.Combine(library, "Black-backup.zip"), ZipArchiveMode.Create))
    zip.CreateEntryFromFile(black, "main.sav");
var bank = "Coleção"; BankStorage.CreateBank(bank);
var bankBox = BankStorage.GetBoxes(bank).First();
for (int i = 0; i < species.Length; i++) BankStorage.WriteSlot(bankBox, i, sav.GetBoxSlotAtIndex(0, i));
var vm = new MainViewModel(new AppSettings { SavesFolder = library, CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1500, Height = 1100 }; win.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Wait(Task task) { for (int i = 0; i < 6000 && !task.IsCompleted; i++) { Pump(); System.Threading.Thread.Sleep(5); } if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); Pump(); }
void Shot(string name) { Pump(); win.CaptureRenderedFrame()!.Save(Path.Combine(output, name + ".png")); Console.WriteLine(name); }
vm.Open(black); Wait(vm.SelectSlotAsync(vm.Boxes.Slots[0])); Shot("boxes"); Shot("friendship");
vm.Editor!.SelectedTab = 2; Shot("moves");
vm.Editor.SelectedTab = 6; Shot("ribbons");
vm.Editor.SelectedTab = 3; Shot("legalmode");
vm.Editor.SelectedTab = 0;
vm.SearchText = "Eevee"; vm.RunSearch(); Shot("search"); vm.SearchText = ""; vm.RunSearch();
vm.CurrentPage = vm.Party; Wait(vm.SelectSlotAsync(vm.Party.Slots[0])); Shot("party");
vm.CurrentPage = vm.Bank; vm.Bank.SelectedBank = bank; vm.Bank.Reload(); Shot("bank");
vm.CurrentPage = vm.Pokedex; Wait(vm.Pokedex.RefreshAsync()); Shot("pokedex");
vm.CurrentPage = vm.Encounters; vm.Encounters.Species = "Eevee"; Wait(vm.Encounters.SearchAsync()); Shot("encounters");
vm.Encounters.Selected = vm.Encounters.Results.First(); Shot("encounter_detail"); vm.Encounters.CloseDetailCommand.Execute(null);
vm.CurrentPage = vm.SaveManager; Wait(vm.SaveManager.RefreshAsync()); Shot("savemanager");
vm.IsHelpOpen = true; Shot("help"); vm.IsHelpOpen = false;
vm.CurrentPage = vm.Boxes; win.Width = 1100; win.Height = 800; Shot("compact"); win.Width = 1500; win.Height = 1100;
var bd = BlankSaveFile.Get(GameVersion.BD); bd.OT = "Demo"; CoreAdapter.Activate(bd);
var feebas = Make(bd, 349); ((IContestStats)feebas).ContestBeauty = 0; ((IContestStats)feebas).ContestSheen = 0; // o botao mostra "Beauty sobe de 0 para 170"
bd.SetBoxSlotAtIndex(feebas, 0, 0); var bdPath = Path.Combine(library, "Feebas.sav"); File.WriteAllBytes(bdPath, bd.Write().ToArray());
vm.Open(bdPath); Wait(vm.SelectSlotAsync(vm.Boxes.Slots[0])); vm.Editor!.SelectedTab = 0; Shot("beauty");
// Interface em ingles: a traducao so e instalada na partida, entao vai por ultimo, numa janela nova.
Loc.Load(Loc.English); Loc.Hook();
var vmEn = new MainViewModel(new AppSettings { SavesFolder = library, CheckForUpdates = false, UiLanguage = Loc.English });
win.Close(); win = new MainWindow { DataContext = vmEn, Width = 1500, Height = 1100 }; win.Show();
vmEn.Open(black); Wait(vmEn.SelectSlotAsync(vmEn.Boxes.Slots[0])); Shot("english");
Console.WriteLine("Capturas sintéticas concluídas: " + output);
