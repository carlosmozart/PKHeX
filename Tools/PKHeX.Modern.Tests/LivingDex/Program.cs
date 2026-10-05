using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
void Check(string n, bool ok, string more = "") { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {n} {more}"); if (!ok) fails++; }
PKM Make(SaveFile sav, ushort sp)
{
    CoreAdapter.Activate(sav);
    return EncounterDatabase.SearchEncounters(sav, sp, true).Where(e => e.Species == sp && e.Version.Contains(sav.Version)).Select(e => EncounterDatabase.ToEntity(sav, e, out _))
        .First(p => p is not null && p.Version == sav.Version && !p.IsEgg && new LegalityAnalysis(p).Valid)!;
}
var black = BlankSaveFile.Get(GameVersion.B); black.OT = "DEMO B";
var white = BlankSaveFile.Get(GameVersion.W); white.OT = "DEMO W";
var native = Make(black, 504); native.CurrentLevel = 10; native.HealPP();
var illegal = native.Clone(); illegal.Move1 = 165; illegal.CurrentLevel = 100;
var foreign = Make(white, 504); foreign.CurrentLevel = 30; foreign.HealPP();
black.SetBoxSlotAtIndex(native, 0, 0); black.SetBoxSlotAtIndex(illegal, 0, 1);
white.SetBoxSlotAtIndex(foreign, 0, 0); white.SetBoxSlotAtIndex(Make(white, 495), 0, 1);
var folder = Path.Combine(work, "saves"); Directory.CreateDirectory(folder);
var pathB = Path.Combine(folder, "Black.sav"); var pathW = Path.Combine(folder, "White.sav");
File.WriteAllBytes(pathB, black.Write().ToArray()); File.WriteAllBytes(pathW, white.Write().ToArray());
var bank = BankStorage.GetBoxes(BankStorage.GetBanks()[0])[0];
var bankPk = native.Clone(); bankPk.CurrentLevel = 15; bankPk.HealPP();
BankStorage.WriteSlot(bank, 0, bankPk); BankStorage.WriteSlot(bank, 1, Make(black, 498));
Dictionary<string, string> Hashes() => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
    .Concat(Directory.EnumerateFiles(BankStorage.Root, "*", SearchOption.AllDirectories)).ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
var hashes = Hashes(); var memoryB = black.Data.ToArray(); var memoryW = white.Data.ToArray();
CoreAdapter.Activate(black);
int progress = 0;
var entries = PokemonDatabase.Build([(pathB, black), (pathW, white)], folder, new(), readOnly: true, sourceProgress: _ => progress++);
Check("sem duplicar saves abertos na pasta", entries.Count == 6, entries.Count.ToString());
Check("progresso por fonte", progress >= 3);
var legal = entries.ToDictionary(e => e, e => LivingDexPlanner.AssessLegality(e, black));
foreach (var e in entries) Console.WriteLine($"{e.Species}: {e.Pkm.Version} · {e.Pkm.CurrentLevel} · {legal[e]} · {e.Source.Name} · {string.Join(" / ", CoreAdapter.GetLegalityIssues(e.Pkm))}");
var plan = LivingDexPlanner.Build(entries, black, false, false, legal);
var patrat = plan.Rows.Single(r => r.Species == 504);
Check("contagem", plan.Owned == 3 && plan.Missing == plan.Rows.Count - 3 && plan.Rows.Count == 649);
Check("legal > nativo > nível", patrat.Candidate is { Source.IsBank: true } && patrat.Candidate.Pkm.CurrentLevel == 15 && patrat.CandidateLegal);
Check("duplicados", patrat.Duplicates.Count == 3 && plan.Duplicates == 3);
var withoutBank = LivingDexPlanner.Build(entries.Where(e => !e.Source.IsBank).ToArray(), black, false, false, legal);
Check("nativo precede nível estrangeiro", withoutBank.Rows.Single(r => r.Species == 504).Candidate?.Source.Version == GameVersion.B);
Check("faltantes", plan.Rows.Single(r => r.Species == 1).Missing);
Check("30 por caixa em ordem nacional", plan.Rows[0].Box == 1 && plan.Rows[0].Slot == 1 && plan.Rows[30].Box == 2 && plan.Rows[30].Slot == 1);
Check("filtro shiny", LivingDexPlanner.Build(entries, black, false, true, legal).Owned == 0);
var forms = LivingDexPlanner.Build([], BlankSaveFile.Get(GameVersion.SW), true, false, new Dictionary<DbEntry, bool>());
Check("formas presentes, sem formas de batalha", forms.Rows.Any(r => r.Species == 201) == PersonalTable.SWSH.IsSpeciesInGame(201)
    && forms.Rows.All(r => PersonalTable.SWSH.IsPresentInGame(r.Species, r.Form) && !FormInfo.IsBattleOnlyForm(r.Species, r.Form, 8)));
Check("saves sintéticos em memória intactos", black.Data.SequenceEqual(memoryB) && white.Data.SequenceEqual(memoryW));
var oldRoot = BankStorage.Root; BankStorage.Root = Path.Combine(work, "absent-bank");
PokemonDatabase.Build([], null, new(), readOnly: true);
Check("leitura não cria Bank", !Directory.Exists(BankStorage.Root)); BankStorage.Root = oldRoot;

var vm = new MainViewModel(new AppSettings { CheckForUpdates = false, SavesFolder = folder });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 950 }; win.Show(); vm.Open(pathB); vm.Open(pathW);
var dex = vm.Pokedex.LivingDex!;
dex.TargetIndex = Array.IndexOf(LivingDexViewModel.Versions.ToArray(), GameVersion.B);
vm.Pokedex.ShowLivingDex = true; vm.CurrentPage = vm.Pokedex;
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
bool Wait(Func<bool> done)
{
    var sw = Stopwatch.StartNew(); while (!done() && sw.ElapsedMilliseconds < 60000) { Pump(); System.Threading.Thread.Sleep(10); } Pump(); return done();
}
Check("plano em segundo plano", Wait(() => !dex.IsBusy && dex.Plan is not null), dex.Progress);
Check("interface conta fontes abertas e Bank", dex.Plan?.Owned == 3 && dex.Plan.Duplicates == 3);
void Shot(string name) { win.UpdateLayout(); for (int i = 0; i < 15; i++) Pump(); win.CaptureRenderedFrame()?.Save(Path.Combine(work, name + ".png")); }
dex.FilterIndex = 3; dex.Selected = dex.Rows.First(); Shot("living-dex-duplicates");
dex.FilterIndex = 1; dex.Selected = dex.Rows.First(); Shot("living-dex-missing");
dex.Selected.FindCommand.Execute(null);
Check("link abre Encontros", vm.CurrentPage == vm.Encounters && vm.Encounters.Species == "Bulbasaur");
object? encSav = null;
for (var type = vm.Encounters.GetType(); type is not null && encSav is null; type = type.BaseType)
    encSav = type.GetProperty("Sav", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)?.GetValue(vm.Encounters);
Check("Encontros continua no save aberto (Usar grava nele)", ReferenceEquals(encSav, vm.ActiveTab!.Sav));
var after = Hashes(); Check("SHA-256 de todos os arquivos intacto", hashes.Count == after.Count && hashes.All(p => after.TryGetValue(p.Key, out var h) && h == p.Value));
win.Close(); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
