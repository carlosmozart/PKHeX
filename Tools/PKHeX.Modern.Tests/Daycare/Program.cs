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
var work = args[0]; BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
PKM Make(SaveFile sav)
{
    ushort species = sav.Version == GameVersion.B ? (ushort)504 : (ushort)25;
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, species, true).Where(e => e.Species == species))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk && new LegalityAnalysis(pk).Valid) return pk;
    throw new Exception("Sem Pikachu legal");
}
foreach (var file in new[] { "cr.sav", "em.sav", "bw.sav" })
{
    var path = Path.Combine(work, file); var sav = CoreAdapter.LoadSave(path)!; CoreAdapter.Activate(sav);
    var pk = Make(sav); var d = Daycares.All(sav)[0]; Daycares.Write(sav, d, 0, sav.BlankPKM); sav.SetBoxSlotAtIndex(pk, 0, 0);
    File.WriteAllBytes(path, sav.Write().ToArray());
    var vm = new MainViewModel(); vm.Open(path); vm.CurrentPage = vm.Party;
    vm.SelectSlotAsync(vm.Boxes.Slots[0]).GetAwaiter().GetResult();
    vm.DepositDaycareAsync(vm.Party.DaycareAreas[0].Slots[0]).GetAwaiter().GetResult();
    Check(file + " depósito move origem", vm.Party.DaycareAreas[0].Slots[0].Occupied && vm.Boxes.Slots[0].IsEmpty && vm.IsDirty);
    vm.UndoCommand.Execute(null);
    Check(file + " desfaz depósito sem duplicar", !vm.Party.DaycareAreas[0].Slots[0].Occupied && !vm.Boxes.Slots[0].IsEmpty);
    vm.RedoCommand.Execute(null);
    Check(file + " refaz depósito", vm.Party.DaycareAreas[0].Slots[0].Occupied && vm.Boxes.Slots[0].IsEmpty);
    vm.EditDaycareAsync(vm.Party.DaycareAreas[0].Slots[0]).GetAwaiter().GetResult();
    int level = pk.CurrentLevel == 100 ? 99 : pk.CurrentLevel + 1;
    vm.Editor!.Level = level; vm.Editor.ApplyCommand.Execute(null);
    Check(file + " editor grava nível legal", vm.Party.DaycareAreas[0].Slots[0].Pokemon.CurrentLevel == level && vm.Editor.IsLegal);
    var area = vm.Party.DaycareAreas[0]; area.EggAvailable = true;
    if (area.Slots[0].HasExperience) area.Slots[0].Experience = 1234;
    vm.Export(path); var read = CoreAdapter.LoadSave(path)!; var rd = Daycares.All(read)[0];
    Check(file + " reabre creche e ovo com checksum", read.ChecksumsValid && Daycares.Read(read, rd, 0).CurrentLevel == level && rd is IDaycareEggState { IsEggAvailable: true });
    if (rd is IDaycareExperience experience) Check(file + " experiência persiste", experience.GetDaycareEXP(0) == 1234);
    vm.WithdrawDaycareAsync(vm.Party.DaycareAreas[0].Slots[0]).GetAwaiter().GetResult();
    Check(file + " retira para caixa", !vm.Party.DaycareAreas[0].Slots[0].Occupied && vm.Boxes.Slots[0].Pkm!.Species == pk.Species);
    vm.UndoCommand.Execute(null);
    Check(file + " desfaz retirada sem duplicar", vm.Party.DaycareAreas[0].Slots[0].Occupied && vm.Boxes.Slots[0].IsEmpty);
    vm.RedoCommand.Execute(null); vm.Export(path); read = CoreAdapter.LoadSave(path)!;
    Check(file + " retirada persiste com checksum", read.ChecksumsValid && !Daycares.All(read)[0].IsDaycareOccupied(0) && read.GetBoxSlotAtIndex(0, 0).Species == pk.Species);
}
foreach (var version in new[] { GameVersion.RD, GameVersion.C, GameVersion.E, GameVersion.CXD, GameVersion.HG, GameVersion.B, GameVersion.X, GameVersion.OR, GameVersion.MN, GameVersion.SH, GameVersion.BD })
{
    var sav = BlankSaveFile.Get(version); var all = Daycares.All(sav);
    Check(version + " oferece creche", all.Length > 0);
    if (version is GameVersion.OR or GameVersion.SH) Check(version + " duas creches", all.Length == 2);
}
var demo = BlankSaveFile.Get(GameVersion.B); CoreAdapter.Activate(demo); var demoPk = Make(demo); Daycares.Write(demo, Daycares.All(demo)[0], 0, demoPk);
var demoPath = Path.Combine(work, "demo.sav"); File.WriteAllBytes(demoPath, demo.Write().ToArray());
var main = new MainViewModel(); main.Open(demoPath); main.CurrentPage = main.Party;
Check("BW esconde seed inexistente", !main.Party.DaycareAreas[0].HasSeed);
var win = new MainWindow { DataContext = main, Width = 1440, Height = 1000 }; win.Show();
for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
win.CaptureRenderedFrame()!.Save(Path.Combine(work, "daycare.png")); win.Close();
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
