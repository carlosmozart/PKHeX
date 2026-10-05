using Avalonia.VisualTree; using System; using System.Diagnostics; using System.IO; using System.Linq; using Avalonia; using Avalonia.Headless; using Avalonia.Threading;
using PKHeX.Modern; using PKHeX.Modern.Services; using PKHeX.Modern.ViewModels; using PKHeX.Modern.Views;
// Animacao da atualizacao (demonstracao, "Continuar usando", bolas cinzas) e backups separados por save de mesmo nome.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(dir, "bank"); SaveBackup.Folder = Path.Combine(dir, "backups");
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {n} {extra}"); if (!ok) fails++; }

// Backups: dois "main" de pastas diferentes nao dividem o limite.
var a = Path.Combine(dir, "switch-a", "main"); var b = Path.Combine(dir, "switch-b", "main");
Directory.CreateDirectory(Path.GetDirectoryName(a)!); Directory.CreateDirectory(Path.GetDirectoryName(b)!);
File.WriteAllBytes(a, [1]); File.WriteAllBytes(b, [2]);
for (int i = 0; i < 22; i++) SaveBackup.BeforeOverwrite(a);
for (int i = 0; i < 3; i++) SaveBackup.BeforeOverwrite(b);
var list = SaveBackup.List();
Check("save A mantém 20 backups", list.Count(x => x.Source == Path.GetFullPath(a)) == 20, $"{list.Count(x => x.Source == Path.GetFullPath(a))}");
Check("save B (mesmo nome) não perde os backups", list.Count(x => x.Source == Path.GetFullPath(b)) == 3);
for (int i = 0; i < 25; i++) SaveBackup.BeforeOverwrite(b);
list = SaveBackup.List();
Check("cada save fica com o próprio limite", list.Count(x => x.Source == Path.GetFullPath(a)) == 20 && list.Count(x => x.Source == Path.GetFullPath(b)) == 20);

// Bolas cinzas e coloridas existem para todos os tipos usados.
Check("bolas cinzas e coloridas", new byte[] { 4, 3, 2, 12, 7, 6, 11, 13, 15, 21, 23, 25, 26, 1 }.All(x => SpriteService.GetGrayBallSprite(x) is not null && SpriteService.GetBallSprite(x) is not null));

// Demonstracao: o painel aparece, chega a 100% e "Continuar usando" esconde.
var vm = new MainViewModel(new AppSettings { CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1280, Height = 800 }; win.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Wait(Func<bool> done, int ms = 30000) { var sw = Stopwatch.StartNew(); while (!done() && sw.ElapsedMilliseconds < ms) { Pump(); System.Threading.Thread.Sleep(10); } Pump(); }
Check("sem download, sem painel", !vm.Help.ShowUpdateOverlay);
vm.Help.DemoAnimationCommand.Execute(null); Pump();
Check("demonstração mostra o painel", vm.Help.ShowUpdateOverlay && vm.Help.IsDemo);
Check("animação está na tela", win.GetVisualDescendants().OfType<PKHeX.Modern.Controls.UpdateAnimation>().Any(c => c.IsEffectivelyVisible));
Wait(() => vm.Help.AnimationDone);
Check("chega a 100% com Pronto", vm.Help.AnimationDone && vm.Help.OverlayText.StartsWith("Pronto"), vm.Help.OverlayText);
vm.Help.HideOverlayCommand.Execute(null); Wait(() => !vm.Help.IsDemo, 5000);
Check("Continuar usando esconde o painel", !vm.Help.ShowUpdateOverlay && !vm.Help.IsDemo);

Console.WriteLine(fails == 0 ? "Todos os testes passaram." : $"{fails} falha(s).");
return fails == 0 ? 0 : 1;
