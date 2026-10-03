using System;
using System.Diagnostics;
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

// Editores por jogo (página Jogo): flags e valores de evento (Gen 2–7) e recordes (Gen 3, 5, 6, 7), gravando e relendo o save.
// Usa CÓPIAS dos saves reais (run-tests.ps1): red, cr, fr, hg, bw, y, moon.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : throw new Exception("Rode pelo run-tests.ps1 (pasta com cópias dos saves).");
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");

var vm = new MainViewModel(new AppSettings { CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int k = 10) { for (int i = 0; i < k; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
bool Wait(Func<bool> done, int ms = 10000) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms && !done()) { Pump(2); if (vm.Dialog is { } d) d.Complete(true); System.Threading.Thread.Sleep(10); } return done(); }
void Shot(string name) { Pump(15); win.UpdateLayout(); Pump(5); win.CaptureRenderedFrame()?.Save(Path.Combine(work, name + ".png")); }

SaveFile Reopen(string file)
{
    var output = Path.Combine(work, "out-" + file);
    Check($"[{file}] exportou", vm.Export(output));
    var sav = CoreAdapter.LoadSave(output)!;
    Check($"[{file}] checksums válidos depois de gravar", sav.ChecksumsValid, sav.ChecksumInfo);
    return sav;
}

// Gen 1: sem página Jogo
vm.Open(Path.Combine(work, "red.sav")); Pump();
Check("[red] Gen 1 não mostra a página Jogo", !vm.Pages.Contains(vm.Game));

foreach (var (file, records) in new[] { ("cr.sav", false), ("fr.sav", true), ("hg.sav", false), ("bw.sav", true), ("y.sav", true), ("moon.sav", true) })
{
    vm.Open(Path.Combine(work, file)); Pump();
    var g = vm.Game;
    Check($"[{file}] página Jogo disponível", vm.Pages.Contains(g), g.Summary);
    vm.CurrentPage = g; Pump();
    Check($"[{file}] sem painel do editor", !vm.ShowEditorPanel);
    Check($"[{file}] flags com nome", g.HasEvents && g.FlagRows.Count > 10 && g.FlagRows.All(f => f.HasName), $"{g.CountText} unnamed={g.ShowUnnamed} nolabels={g.NoLabels} lang={GameInfo.CurrentLanguage}");
    Check($"[{file}] recordes {(records ? "presentes" : "ausentes")}", g.HasRecords == records, g.Summary);

    // Flag: inverte a primeira com nome e confere depois de gravar
    var flag = g.FlagRows[0];
    bool before = flag.IsSet;
    flag.IsSet = !before;
    Check($"[{file}] mudar flag marca alteração", vm.IsDirty);

    // Busca e categoria
    g.Query = flag.Number; Pump();
    Check($"[{file}] busca pelo número", g.FlagRows.Count == 1 && g.FlagRows[0].Index == flag.Index, g.CountText);
    g.Query = ""; g.ShowUnnamed = true; Pump();
    Check($"[{file}] mostrar sem nome = todas", g.FlagRows.Count == vm.ActiveTab!.Sav switch { IEventFlagArray a => a.EventFlagCount, IEventFlagProvider37 p => p.EventWork.EventFlagCount, _ => -1 }, g.CountText);
    g.ShowUnnamed = false;

    // Valor de evento: o primeiro com nome
    int workIndex = -1, workValue = 0;
    g.Tab = 1; Pump();
    if (g.WorkRows.FirstOrDefault() is { } w)
    {
        workIndex = w.Index;
        workValue = (w.Value + 1) % 3;
        w.ValueNumber = workValue;
        Check($"[{file}] valor de evento alterado", w.Value == workValue);
    }

    // Recorde: o primeiro com nome (passos)
    int recordId = -1; long recordValue = 0, otherBefore = 0; int otherId = -1;
    if (records)
    {
        g.Tab = 2; Pump();
        var r = g.RecordRows[0];
        var other = g.RecordRows[1];
        recordId = r.Id; otherId = other.Id; otherBefore = other.Value;
        recordValue = r.Value + 1234;
        r.ValueNumber = recordValue;
        Check($"[{file}] recorde alterado", r.Value == recordValue, $"{r.Name} = {r.Value}");
        if (file == "fr.sav")
            Shot("game-records");
    }
    if (file == "fr.sav")
    {
        g.Tab = 0; g.CategoryIndex = Math.Max(0, g.Categories.ToList().IndexOf("Itens escondidos")); Pump();
        Check("[fr.sav] categoria filtra", g.FlagRows.Count > 0 && g.FlagRows.All(f => f.Category == "Itens escondidos"), g.CountText);
        Shot("game-flags");

        // Ativar mostradas (com pergunta)
        g.OnlySet = false;
        int off = g.FlagRows.Count(f => !f.IsSet);
        g.SetVisibleCommand.Execute("1");
        Check("[fr.sav] ativar mostradas", Wait(() => g.FlagRows.All(f => f.IsSet)), $"{off} desativadas antes");
        g.CategoryIndex = 0;
    }

    // Grava, reabre e confere
    var saved = Reopen(file);
    var ed = new GameEditors(saved);
    Check($"[{file}] flag gravada", ed.GetFlag(flag.Index) == !before);
    if (workIndex >= 0)
        Check($"[{file}] valor gravado", ed.GetWork(workIndex) == workValue);
    if (records)
    {
        var rec = ed.Records.First(x => x.Id == recordId);
        Check($"[{file}] recorde gravado", ed.GetRecord(rec) == recordValue, ed.GetRecord(rec).ToString());
        Check($"[{file}] outro recorde intacto", ed.GetRecord(ed.Records.First(x => x.Id == otherId)) == otherBefore);
    }
}

Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
