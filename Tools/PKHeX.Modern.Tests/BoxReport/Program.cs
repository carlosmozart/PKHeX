using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; Directory.CreateDirectory(work); int fails = 0;
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
bool Run(Func<Task<bool>> action)
{
    var task = action(); var start = DateTime.UtcNow;
    while (!task.IsCompleted && DateTime.UtcNow - start < TimeSpan.FromSeconds(60)) { Pump(); System.Threading.Thread.Sleep(5); }
    return task.IsCompletedSuccessfully && task.Result;
}
var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "DEMO"; CoreAdapter.Activate(sav);
var pk = new PK5 { Species = 504, CurrentLevel = 12, Version = GameVersion.B, OriginalTrainerName = "DEMO", Nickname = "Olá,\"Ana\"", IsNicknamed = true };
sav.SetPartySlotAtIndex(pk.Clone(), 0); sav.SetBoxSlotAtIndex(pk.Clone(), 0, 4);
var egg = pk.Clone(); egg.IsEgg = true; sav.SetBoxSlotAtIndex(egg, 1, 8);
var validPokemon = EncounterDatabase.SearchEncounters(sav, 504, true).Where(e => e.Species == 504)
    .Select(e => EncounterDatabase.ToEntity(sav, e, out _)).First(p => p is { IsEgg: false } && new LegalityAnalysis(p).Valid)!;
sav.SetBoxSlotAtIndex(validPokemon, 0, 9);
var path = Path.Combine(work, "Black.sav"); File.WriteAllBytes(path, sav.Write().ToArray());
BankStorage.CreateBank("Alpha"); BankStorage.CreateBank("Beta");
var box = BankStorage.GetBoxes("Alpha")[0]; BankStorage.WriteSlot(box, 2, pk.Clone());
BankStorage.CreateBox("Alpha", "Segunda"); BankStorage.WriteSlot(BankStorage.GetBoxes("Alpha")[1], 5, validPokemon.Clone());
BankStorage.WriteSlot(BankStorage.GetBoxes("Beta")[0], 0, pk.Clone());
Dictionary<string, string> Hashes() => Directory.GetFiles(work, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".sav") || f.Contains(Path.DirectorySeparatorChar + "bank" + Path.DirectorySeparatorChar)).ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
var original = Hashes(); var data = sav.Data.ToArray();
var entries = BoxReport.ReadSave(sav.Clone());
Check("party then boxes, no empty slots, actual egg retained", entries.Count == 4 && entries[0].Location.Contains("Equipe") && entries[1].Location.Contains("slot 5") && entries[2].Location.Contains("slot 10") && entries[3].Pokemon.IsEgg);
var rows = BoxReport.ReadBank("Alpha"); Check("only selected bank, all boxes", rows.Count == 2 && rows.All(e => e.Location.StartsWith("Alpha")));
var legal = entries.Select(e => new LegalityAnalysis(e.Pokemon).Valid).ToArray();
Check("mixed legal and illegal records", legal.Contains(true) && legal.Contains(false));
var csv = BoxReport.Encode(entries, legal); var text = Encoding.UTF8.GetString(csv.AsSpan(3));
Check("UTF8 BOM and accent", csv.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }) && text.Contains("Olá"));
Check("quote and comma escaping", text.Contains("\"Olá,\"\"Ana\"\"\""));
Check("one header, four rows", text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length == 5);
Check("30 columns", BoxReport.Headers.Count == 30);
Check("source bytes unchanged", sav.Data.SequenceEqual(data));
var oldPokemon = BlankSaveFile.Get(GameVersion.C).BlankPKM; oldPokemon.Species = 1;
var oldCsv = Encoding.UTF8.GetString(BoxReport.Encode([new("Gen 2", oldPokemon, false)], [false])).Split("\r\n")[1].Split(',');
Check("Gen 2 has no invented nature or ability", oldCsv[7] == "" && oldCsv[8] == "");

var vm = new MainViewModel(new AppSettings { CheckForUpdates = false }); vm.Open(path); vm.Bank.SelectedBank = "Alpha";
var active = vm.ActiveTab!.Sav;
var property = typeof(ParseSettings).GetProperty("ActiveTrainer", BindingFlags.NonPublic | BindingFlags.Static)!;
var trainer = property.GetValue(null);
ParseSettings.AllowEraCartGB = true; ParseSettings.AllowEraCartGBA = false; ParseSettings.AllowEraSwitchGBA = true;
byte[]? written = null;
vm.SaveBoxReport = (bytes, _) => { written = bytes; return Task.FromResult(true); };
Check("save export completed", Run(() => vm.ExportBoxReportAsync(false)));
Check("CSV legality matches the active context", written is not null && Encoding.UTF8.GetString(written).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.EndsWith(",Legal")).SequenceEqual(BoxReport.ReadSave(active.Clone()).Select(e => new LegalityAnalysis(e.Pokemon).Valid)));
Check("bank export completed", Run(() => vm.ExportBoxReportAsync(true)));
Check("bank legality matches inspection", written is not null && Encoding.UTF8.GetString(written).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.EndsWith(",Legal")).SequenceEqual(BoxReport.ReadBank("Alpha").Select(e => BankInspection.Analyze(e.Pokemon, active).Valid)));
Check("trainer and active tab unchanged", ReferenceEquals(active, vm.ActiveTab!.Sav) && ReferenceEquals(trainer, property.GetValue(null)));
Check("all flags preserved", ParseSettings.AllowGBEraEvents && !ParseSettings.AllowGBACrossTransferRSE(pk) && ParseSettings.AllowGen3EventTicketsAll(pk));
Check("does not mark dirty", !vm.IsDirty);
Check("original hashes unchanged", original.All(e => Hashes().TryGetValue(e.Key, out var hash) && hash == e.Value) && original.Count == Hashes().Count);
Loc.Load(Loc.English);
Check("English headers", BoxReport.Headers[0] == "Location" && BoxReport.Headers[1] == "Species" && BoxReport.Headers[^1] == "Legality");
Loc.Load(Loc.Portuguese);
vm.CurrentPage = vm.Boxes;
var desktop = new PKHeX.Modern.Views.MainWindow { DataContext = vm, Width = 1440, Height = 950 }; desktop.Show(); for (int i = 0; i < 10; i++) Pump(); desktop.CaptureRenderedFrame()?.Save(Path.Combine(work, "report-desktop.png")); desktop.Hide();
App.ShowShortcuts = false;
var phone = new Avalonia.Controls.Window { Width = 892, Height = 412, Content = new PKHeX.Modern.Views.MobileShell(vm) }; phone.Show(); for (int i = 0; i < 10; i++) Pump(); phone.CaptureRenderedFrame()?.Save(Path.Combine(work, "report-mobile.png"));
vm.CurrentPage = vm.Bank; for (int i = 0; i < 10; i++) Pump(); phone.CaptureRenderedFrame()?.Save(Path.Combine(work, "report-bank-mobile.png")); phone.Close();
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
