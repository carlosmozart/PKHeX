// Renderiza o PKHeX.Modern sem abrir janela (Avalonia headless) e salva PNGs.
// Uso: dotnet run --project Tools/PKHeX.Modern.Render -- <save> <pastaSaida>
using System; using System.IO; using System.Linq;
using Avalonia; using Avalonia.Headless; using Avalonia.Threading;
using PKHeX.Modern; using PKHeX.Modern.ViewModels; using PKHeX.Modern.Views;
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var outDir = args[1];
void Run(string prefix, int w, int h, bool light = false)
{
    var vm = new MainViewModel(); vm.Open(args[0]);
    var win = new MainWindow { DataContext = vm, Width = w, Height = h };
    win.Show();
    void Shot(string name) { for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } win.CaptureRenderedFrame()!.Save(Path.Combine(outDir, prefix + name)); }
    vm.Boxes.SelectSlotCommand.Execute(vm.Boxes.Slots[1]); Shot("boxes.png");
    vm.Boxes.Slots[8].IsDropTarget = true; Shot("drag.png"); vm.Boxes.Slots[8].IsDropTarget = false;
    vm.CurrentPage = vm.Party; vm.Party.SelectSlotCommand.Execute(vm.Party.Slots[0]); Shot("party.png");
    if (prefix == "")
        for (int t = 1; t < 6; t++) { vm.Editor!.SelectedTab = t; Shot($"tab{t}.png"); }
    win.Close();
}
Run("", 1600, 950);
Run("small_", 1100, 720);
// editor completo, janela alta
var vm2 = new MainViewModel(); vm2.Open(args[0]);
var w2 = new MainWindow { DataContext = vm2, Width = 1600, Height = 1900 }; w2.Show();
vm2.Boxes.SelectSlotCommand.Execute(vm2.Boxes.Slots[1]);
for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
w2.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "editor_full.png"));
Console.WriteLine("ok");
