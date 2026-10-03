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

// Pagina Jogo no Scarlet/Violet (blocos com nome) e no BD/SP (flags, flags de sistema, valores e atalhos), gravando e relendo.
// Scarlet: COPIA do save real (sv.sav, pelo run-tests.ps1; pulado sem ele). BD/SP: save sintetico.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-gameevents-" + Guid.NewGuid()); Directory.CreateDirectory(work);
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

// ---------- Scarlet/Violet ----------
var svPath = Path.Combine(work, "sv.sav");
if (File.Exists(svPath))
{
    vm.Open(svPath); Pump();
    var g = vm.Game;
    Check("[SV] página Jogo disponível", vm.Pages.Contains(g), g.Summary);
    vm.CurrentPage = g; Pump();
    Check("[SV] centenas de flags e valores com nome", g.HasEvents && g.HasWorks && vm.Game.FlagRows.Count > 500, g.Summary);
    Check("[SV] categoria Voo", g.Categories.Contains("Voo") && g.Categories.Contains("Receitas de TM"), string.Join(", ", g.Categories));
    g.CategoryIndex = g.Categories.ToList().IndexOf("Voo"); Pump();
    Check("[SV] filtro Voo só traz pontos de voo", g.FlagRows.Count > 20 && g.FlagRows.All(f => f.Name.StartsWith("FSYS_YMAP_FLY")), g.CountText);
    var fly = g.FlagRows.First();
    var flyName = fly.Name; var flyBefore = fly.IsSet;
    fly.IsSet = !flyBefore; Pump();
    Check("[SV] flag trocada e save marcado", fly.IsSet == !flyBefore && vm.IsDirty);
    Shot("sv-flags");
    g.CategoryIndex = 0; g.Query = "Can Craft TM"; Pump();
    Check("[SV] busca pelo nome humanizado", g.FlagRows.Count > 100 && g.FlagRows.All(f => f.Name.StartsWith("Can Craft TM")), g.CountText);
    g.Query = ""; g.Tab = 1; Pump();
    var wrow = g.WorkRows.First(w => w.Name.StartsWith("WEVT_"));
    var wName = wrow.Name; var wNew = wrow.Value == 7 ? 8 : 7;
    wrow.ValueNumber = wNew; Pump();
    Check("[SV] valor alterado", wrow.Value == wNew, $"{wName} = {wrow.Value}");
    Shot("sv-works");
    var saved = Reopen("sv.sav");
    var ed = new GameEditors(saved);
    Check("[SV] flag gravada", ed.GetFlag(ed.Flags.First(f => f.Name == flyName).Index) == !flyBefore, flyName);
    Check("[SV] valor gravado", ed.GetWork(ed.Works.First(w => w.Name == wName).Index) == wNew, wName);
    Check("[SV] SUSHI_DAMMY fica de fora", !ed.Flags.Any(f => f.Name.StartsWith("SUSHI_DAMMY")));
}
else
    Console.WriteLine("(sem sv.sav: parte do Scarlet pulada)");

// ---------- BD/SP (sintetico) ----------
var bdPath = Path.Combine(work, "BD.sav");
var bd0 = (SAV8BS)BlankSaveFile.Get(GameVersion.BD); bd0.OT = "Demo";
File.WriteAllBytes(bdPath, bd0.Write().ToArray());
vm.Open(bdPath); Pump();
var gb = vm.Game;
vm.CurrentPage = gb; Pump();
Check("[BDSP] página Jogo com flags, valores, recordes e atalhos", gb.HasEvents && gb.HasWorks && gb.HasRecords && gb.HasShortcuts, gb.Summary);
gb.ShowUnnamed = true; Pump();
Check("[BDSP] 4000 flags + 1000 de sistema", gb.FlagRows.Count == 5000, gb.CountText);
var sys = gb.FlagRows.First(f => f.Number == "S#0005");
sys.IsSet = true; Pump();
Check("[BDSP] flag de sistema S#0005 (jogo terminado)", ((SAV8BS)vm.ActiveTab!.Sav).FlagWork.GetSystemFlag(5));
gb.ShowUnnamed = false; gb.Tab = 3; Pump();
var darkrai = gb.ShortcutRows.First(s => s.Name.Contains("Darkrai"));
Check("[BDSP] atalho do Darkrai disponível", darkrai.IsReady);
darkrai.ApplyCommand.Execute(null);
Check("[BDSP] Darkrai liberado (pergunta respondida)", Wait(() => !darkrai.IsReady), darkrai.State);
Shot("bdsp-shortcuts");
var bdSaved = (SAV8BS)Reopen("BD.sav");
Check("[BDSP] Member Card e evento gravados", bdSaved.FlagWork.GetWork(275) == 1 && bdSaved.Items.GetItemQuantity(454) == 1 && bdSaved.Zukan.HasNationalDex);
Check("[BDSP] flag de sistema gravada", bdSaved.FlagWork.GetSystemFlag(5));
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
