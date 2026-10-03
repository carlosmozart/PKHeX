using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0];
SaveBackup.Folder = Path.Combine(dir, "backups");
BankStorage.Root = Path.Combine(dir, "bank");
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
var vm = new MainViewModel();
var win = new MainWindow { DataContext = vm, Width = 1500, Height = 950 }; win.Show();
foreach (var file in new[] { "fr.sav", "hg.sav", "sh.sav" })
{
    vm.Open(Path.Combine(dir, file)); Pump();
    Check(file + " abrir não altera o save", !vm.IsDirty);
    var boxes = vm.Boxes;
    Check(file + " controles disponíveis", boxes.CanRename && boxes.HasWallpapers);
    boxes.Prompt = (_, _, _) => Task.FromResult<string?>("Colecao");
    boxes.RenameCommand.Execute(null); Pump();
    Check(file + " nome e aba atualizados", boxes.BoxName == "Colecao" && boxes.BoxTabs[0].Name == "Colecao" && vm.IsDirty);
    int wallpaper = boxes.WallpaperIndex == 1 ? 2 : 1;
    boxes.WallpaperIndex = wallpaper; Pump();
    Check(file + " papel de parede", boxes.WallpaperIndex == wallpaper && boxes.Wallpaper is not null);
    boxes.CurrentBox = 1; boxes.CurrentBox = 0;
    Check(file + " troca de caixa preserva", boxes.BoxName == "Colecao" && boxes.WallpaperIndex == wallpaper);
    var output = Path.Combine(dir, "edited-" + file);
    vm.Export(output);
    var reopened = CoreAdapter.LoadSave(output)!;
    Check(file + " persistência no arquivo", ((IBoxDetailNameRead)reopened).GetBoxName(0) == "Colecao" && ((IBoxDetailWallpaper)reopened).GetBoxWallpaper(0) == wallpaper);
    boxes.Prompt = (_, _, _) => Task.FromResult<string?>(null);
    boxes.RenameCommand.Execute(null);
    Check(file + " cancelar mantém nome", boxes.BoxName == "Colecao");
    Pump(); win.CaptureRenderedFrame()!.Save(Path.Combine(dir, file + ".png"));
}
var first = vm.OpenSaves[0];
vm.SwitchToAsync(first).GetAwaiter().GetResult(); Pump();
Check("troca de aba preserva nome", vm.Boxes.BoxName == "Colecao");
foreach (var version in new[] { GameVersion.RD, GameVersion.C, GameVersion.PLA, GameVersion.ZA })
{
    var sav = BlankSaveFile.Get(version);
    vm.Boxes.Load(sav);
    Check(version + " suporte", vm.Boxes.CanRename == (sav is IBoxDetailName));
    Check(version + " sem wallpaper editável", !vm.Boxes.HasWallpapers);
}
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
