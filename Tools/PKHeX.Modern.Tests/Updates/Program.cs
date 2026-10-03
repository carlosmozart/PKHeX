using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
var release = UpdateChecker.ParseLatest("""
[{"tag_name":"modern-v99.0.0","name":"Novidades","html_url":"https://github.com/carlosmozart/PKHeX/releases/tag/modern-v99.0.0","body":"## Caixas\n- **Renomear caixas** e escolher papel de parede.\n\n## Saves\n- Navegação por teclado.","assets":[]}]
""")!;
Check("API preserva notas", release.Notes!.Contains("Renomear caixas"));
var vm = new MainViewModel();
var win = new MainWindow { DataContext = vm, Width = 1100, Height = 720 }; win.Show();
vm.Help.SetLatest(release);
var pending = vm.Help.InstallUpdateAsync(); Pump();
Check("notas antes de baixar", !pending.IsCompleted && vm.Dialog is { HasDetails: true } && !vm.Help.IsDownloading);
Check("conteúdo legível", vm.Dialog!.Details[1] == "- Renomear caixas e escolher papel de parede.");
Check("comandos bloqueados durante janela", !vm.Help.InstallUpdateCommand.CanExecute(null) && !vm.Help.OpenLatestCommand.CanExecute(null));
await vm.Help.InstallUpdateAsync();
win.CaptureRenderedFrame()!.Save(Path.Combine(args[0], "update-notes.png"));
vm.Dialog!.Complete(false); pending.GetAwaiter().GetResult(); Pump();
Check("Depois cancela e mantém aviso", vm.Dialog is null && vm.Help.ShowUpdateBanner && !vm.Help.IsDownloading && !vm.Help.IsReviewingUpdate);
vm.Help.SetLatest(release with { Notes = null });
pending = vm.Help.InstallUpdateAsync(); Pump();
Check("release sem notas tem alternativa", vm.Dialog!.Details[0].Contains("não tem notas"));
vm.Dialog.Complete(false); pending.GetAwaiter().GetResult();
var other = vm.ConfirmAsync("Outra pergunta", "Mensagem", "OK");
pending = vm.Help.InstallUpdateAsync(); pending.GetAwaiter().GetResult();
Check("atualização preserva pergunta existente", vm.Dialog!.Title == "Outra pergunta");
vm.Dialog.Complete(false); other.GetAwaiter().GetResult();
vm.Help.SetLatest(release with { Version = UpdateChecker.Current });
await vm.Help.InstallUpdateAsync();
Check("versão atual não abre janela", vm.Dialog is null);
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
Console.WriteLine("Capturas: " + args[0]);
return fails == 0 ? 0 : 1;
