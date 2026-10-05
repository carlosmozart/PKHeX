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
using PKHeX.Modern.Controls;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
void Check(string n, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {n}"); if (!ok) fails++; }
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
void Wait(Func<bool> done) { var sw = Stopwatch.StartNew(); while (!done() && sw.ElapsedMilliseconds < 10000) { Pump(); System.Threading.Thread.Sleep(10); } Check("operação terminou", done()); Pump(); }
var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "DEMO";
for (int i = 0; i < 3; i++) sav.SetBoxSlotAtIndex(new PK5 { Species = (ushort)(504+i), Version = GameVersion.B, CurrentLevel = 10, OriginalTrainerName = "DEMO" }, 0, i);
var path = Path.Combine(work, "Black.sav"); File.WriteAllBytes(path, sav.Write().ToArray());
var vm = new MainViewModel(new AppSettings { CheckForUpdates = false }); vm.Open(path); vm.LegalMode = false; vm.CurrentPage = vm.Boxes;
var desktop = new MainWindow { DataContext = vm, Width = 1440, Height = 950 }; desktop.Show(); Pump();
Check("desktop mantém tooltips e templates", desktop.GetVisualDescendants().OfType<Button>().Any(b => b.Content is string && ToolTip.GetTip(b) is string && b.ContentTemplate is null));
Check("desktop sem barra de toque", !vm.ShowTouchToolbar);
var ctrlSlot = desktop.GetVisualDescendants().OfType<Button>().First(b => b.DataContext == vm.Boxes.Slots[0]);
var point = ctrlSlot.TranslatePoint(new Point(ctrlSlot.Bounds.Width / 2, ctrlSlot.Bounds.Height / 2), desktop)!.Value;
desktop.MouseDown(point, MouseButton.Left, RawInputModifiers.Control); desktop.MouseUp(point, MouseButton.Left, RawInputModifiers.Control); Pump();
Check("Ctrl+clique marca sem editor", vm.MarkedCount == 1 && vm.Editor is null); vm.ClearMarks(); desktop.Close();

App.ShowShortcuts = false; vm.IsTouchUI = true;
var shell = new MobileShell(vm); var phone = new Window { Width = 892, Height = 412, Content = shell }; phone.Show(); Pump();
void Tap(SlotViewModel slot, bool hold = false, bool scroll = false)
{
    phone.UpdateLayout(); Pump();
    var b = phone.GetVisualDescendants().OfType<Button>().First(b => b.DataContext == slot);
    var p = b.TranslatePoint(new Point(b.Bounds.Width/2, 12), phone)!.Value;
    phone.MouseDown(p, MouseButton.Left);
    if (hold) { using var stop = new System.Threading.CancellationTokenSource(650); Dispatcher.UIThread.MainLoop(stop.Token); }
    if (scroll) phone.MouseMove(p + new Vector(0, 35));
    phone.MouseUp(scroll ? p + new Vector(0, 35) : p, MouseButton.Left); Pump();
}
Check("barra de toque visível", vm.ShowTouchToolbar);
vm.TouchSelectionMode = true;
for (int i = 0; i < 3; i++) Tap(vm.Boxes.Slots[i]);
Check("três slots por toque", vm.MarkedCount == 3 && vm.Boxes.Slots.Take(3).All(s => s.IsMarked));
phone.UpdateLayout(); Pump(); phone.CaptureRenderedFrame()?.Save(Path.Combine(work, "touch-selection.png"));
vm.SelectionBoxIndex = 1; vm.TouchDropIndex = 1; vm.MoveTouchSelectionCommand.Execute(null);
Wait(() => vm.Boxes.Slots.Count(s => !s.IsEmpty) == 3);
Check("ação de grupo recebeu os três", vm.Boxes.CurrentBox == 1 && vm.Boxes.Slots.Count(s => !s.IsEmpty) == 3 && vm.ActiveTab!.Sav.GetBoxSlotAtIndex(0,0).Species != 0);
vm.TouchSelectionMode = false; Check("sair limpa marcas", vm.MarkedCount == 0);
Tap(vm.Boxes.Slots[0], hold: true); Check("toque longo liga e marca", vm.TouchSelectionMode && vm.MarkedCount == 1);
vm.TouchSelectionMode = false; Tap(vm.Boxes.Slots[0], scroll: true);
Check("rolagem não marca nem abre editor", vm.MarkedCount == 0 && vm.Editor is null);
vm.CurrentPage = vm.Bank; Pump(); vm.TouchDropIndex = 1;
vm.CurrentPage = vm.Boxes; vm.TouchSelectionMode = true; for (int i = 0; i < 3; i++) Tap(vm.Boxes.Slots[i]);
vm.BankTouchSelectionCommand.Execute(null); Wait(() => vm.Bank.Slots.Count(s => !s.IsEmpty) == 3);
vm.CurrentPage = vm.Bank; Pump(); vm.ClearMarks(); for (int i = 0; i < 3; i++) Tap(vm.Bank.Slots[i]);
Check("seleção no Bank", vm.MarkedCount == 3); vm.TouchSelectionMode = false; Check("sair do Bank limpa marcas", !vm.HasMarks);

var prefs = shell.View.FindControl<Button>("PrefsButton")!; prefs.Flyout!.ShowAt(prefs); Pump();
var flyoutContent = ((Flyout)prefs.Flyout).Content as Control;
Check("descrições disponíveis sem hover", flyoutContent!.GetVisualDescendants().OfType<TouchHint>().Any());
phone.UpdateLayout(); for (int i=0;i<8;i++) Pump(); phone.CaptureRenderedFrame()?.Save(Path.Combine(work, "touch-preferences.png")); prefs.Flyout.Hide();
vm.CurrentPage = vm.Game; Pump();
Check("cabeçalhos de Jogo com informação", phone.GetVisualDescendants().OfType<TouchHint>().Any());
Check("listas com inércia e barra visível", phone.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsVisible).All(s => ScrollViewer.GetIsScrollInertiaEnabled(s) && !s.AllowAutoHide));
phone.Close(); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
