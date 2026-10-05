using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
void Check(string n, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {n}"); if (!ok) fails++; }
var log = Path.Combine(work, "crash.log");
File.WriteAllLines(log, Enumerable.Range(0, 60).SelectMany(i => new[]
{
    $"[2026-10-05 12:00:{i:00}] System.IO.IOException: TRAINER_SECRET {Environment.UserName} save={{rawBytes}}",
    $"   at Demo.Read(String path) in C:\\Users\\{Environment.UserName}\\TRAINER_SECRET\\Save.cs:line {i}",
}));
var settings = new AppSettings { CheckForUpdates = false, ThemeKey = "pixel", UiLanguage = "pt-BR" };
var text = DiagnosticReport.Build(settings, "Pasta TRAINER_SECRET: 60 arquivo(s), 40 lido(s) como possível save, 20 ignorado(s) pelo tamanho, 2 com erro (ex.: TRAINER_SECRET.sav)", log, ["TRAINER_SECRET"]);
Check("versão incluída", text.Contains(UpdateChecker.CurrentText));
Check("idioma e tema", text.Contains("pt-BR") && text.Contains("pixel"));
Check("últimas 50 linhas", text.Contains("12:00:59") && !text.Contains("12:00:01") && text.Split("System.IO.IOException").Length == 26);
Check("sem treinador, usuário, caminhos ou dados", !text.Contains("TRAINER_SECRET") && !text.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase) && !text.Contains("C:\\Users") && !text.Contains("rawBytes"));
Check("resumo agregado", text.Contains("60 arquivo(s)") && text.Contains("2 com erro"));
Check("registro ausente", DiagnosticReport.Build(settings, logPath: Path.Combine(work, "missing.log")).Contains("Nenhum erro registrado"));
var vm = new MainViewModel(settings); var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
vm.Help.PrepareDiagnostic = () => text; string? sent = null;
vm.Help.SendDiagnostic = value => { sent = value; return Task.FromResult(true); };
vm.OpenHelpCommand.Execute(null); vm.Help.SelectedTab = 2;
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
void Wait(Task task, bool answer, bool capture)
{
    var sw = Stopwatch.StartNew();
    while (!task.IsCompleted && sw.ElapsedMilliseconds < 20000)
    {
        Pump();
        if (vm.Dialog is { } d)
        {
            Check("prévia mostra o texto exato", string.Join(Environment.NewLine, d.Details) == text);
            if (capture) { win.UpdateLayout(); for (int i = 0; i < 10; i++) Pump(); win.CaptureRenderedFrame()?.Save(Path.Combine(work, "diagnostic-preview.png")); }
            d.Complete(answer);
        }
        System.Threading.Thread.Sleep(10);
    }
    Check("ação terminou", task.IsCompleted);
}
Wait(vm.Help.ShareDiagnosticAsync(), false, true); Check("cancelar não envia", sent is null);
Wait(vm.Help.ShareDiagnosticAsync(), true, false); Check("envia exatamente a prévia", sent == text);
win.Close(); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
