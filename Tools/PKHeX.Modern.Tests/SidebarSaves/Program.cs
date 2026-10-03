using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

// Sem save aberto: a barra lateral lista os saves da pasta (Save Manager) e abre com um clique. Saves sinteticos.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-sidesaves-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
var lib = Path.Combine(work, "saves"); Directory.CreateDirectory(lib);
foreach (var (v, n) in new[] { (GameVersion.B, "Black.sav"), (GameVersion.W, "White.sav") })
{
    var s = BlankSaveFile.Get(v); s.OT = "Demo"; File.WriteAllBytes(Path.Combine(lib, n), s.Write().ToArray());
}
var empty = Path.Combine(work, "vazia"); Directory.CreateDirectory(empty);

var settings = new AppSettings { CheckForUpdates = false, SavesFolder = lib };
var vm = new MainViewModel(settings);
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int n = 10) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
bool Wait(Func<bool> done, int ms = 15000) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms && !done()) { Pump(2); if (vm.Dialog is { } d) d.Complete(true); System.Threading.Thread.Sleep(10); } return done(); }
Button[] SideSaves() => [.. win.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("sideSave") && b.IsEffectivelyVisible)];
bool SaveAsVisible() => win.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Salvar como..." && b.IsEffectivelyVisible);

Check("lista da pasta carregada", Wait(() => SideSaves().Length == 2), $"{SideSaves().Length} saves");
Check("sem save aberto: “Salvar como...” escondido", !SaveAsVisible());
win.CaptureRenderedFrame()?.Save(Path.Combine(work, "sem-save.png"));

// clique real no segundo save da barra lateral
var target = SideSaves()[1];
var p = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), win)!.Value;
var expected = ((SaveEntryViewModel)target.DataContext!).FileName;
win.MouseDown(p, MouseButton.Left); win.MouseUp(p, MouseButton.Left);
Check("clique abre o save", Wait(() => vm.HasSave), vm.ActiveTab?.FileName);
Check("abriu o save clicado", vm.ActiveTab?.FileName == expected, $"{vm.ActiveTab?.FileName} / {expected}");
Pump(10);
Check("com save aberto: lista some e “Salvar como...” volta", SideSaves().Length == 0 && SaveAsVisible() && !vm.ShowSidebarSaves);

// fechou tudo: a lista volta
var close = vm.CloseTabAsync(vm.ActiveTab!); Wait(() => close.IsCompleted);
Check("fechou o save: lista volta", Wait(() => SideSaves().Length == 2), $"{SideSaves().Length}");

// pasta vazia: nada na barra lateral
vm.SaveManager.Folder = empty;
Check("pasta vazia: sem lista", Wait(() => !vm.ShowSidebarSaves && SideSaves().Length == 0));
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
