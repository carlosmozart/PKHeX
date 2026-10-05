using System;
using System.Collections.Generic;
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

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
void Wait(Func<bool> done, Action? step = null) { var watch = Stopwatch.StartNew(); while (!done() && watch.ElapsedMilliseconds < 60000) { Pump(); step?.Invoke(); System.Threading.Thread.Sleep(10); } Check("operação terminou", done()); Pump(); }
var pk = new PK8 { Species = 25, Version = GameVersion.SW, Nature = Nature.Hardy, StatAlignment = Nature.Hardy,
    Nickname = "Pikachu", OriginalTrainerName = "DEMO", Language = 2, PID = 0x12345678, TID16 = 100, SID16 = 200,
    MetLocation = 1, MetLevel = 5, MetDate = new DateOnly(2025, 1, 1), Ability = 9, Ball = 4, CurrentLevel = 10 };
var checks = new Dictionary<string, Action<PK8>>
{
    ["Espécie/forma"] = p => p.Species = 26, ["Nível"] = p => p.CurrentLevel = 20,
    ["Natureza"] = p => p.Nature = Nature.Adamant, ["Menta"] = p => p.StatAlignment = Nature.Jolly,
    ["Habilidade"] = p => p.Ability = 31, ["PID"] = p => p.PID++, ["Shiny"] = p => p.SetShiny(),
    ["IVs"] = p => p.IV_HP = 31, ["EVs"] = p => p.EV_HP = 4, ["Golpes"] = p => p.Move1 = 85,
    ["Item"] = p => p.HeldItem = 1, ["Bola"] = p => p.Ball = 3, ["OT"] = p => p.OriginalTrainerName = "OTHER",
    ["TID"] = p => p.TID16++, ["SID"] = p => p.SID16++, ["Idioma"] = p => p.Language = 3,
    ["Apelido"] = p => p.Nickname = "Sparky", ["Local do encontro"] = p => p.MetLocation = 2,
    ["Nível do encontro"] = p => p.MetLevel = 10, ["Data do encontro"] = p => p.MetDate = new DateOnly(2025, 2, 1),
    ["Jogo de origem"] = p => p.Version = GameVersion.SH, ["Fitas e marcas"] = p => p.RibbonChampionGalar = true,
    ["Pokérus"] = p => p.PokerusStrain = 1,
};
foreach (var (field, edit) in checks)
{
    var copy = (PK8)pk.Clone(); edit(copy);
    Check(field, PokemonDiff.Compare(pk, copy).Any(c => c.Field == field));
}
var hp = new PK5 { Species = 25, IV_HP = 31 }; var hpAfter = hp.Clone(); hpAfter.HPType = (hp.HPType + 1) % 16;
Check("Hidden Power", PokemonDiff.Compare(hp, hpAfter).Any(c => c.Field == "Hidden Power"));
Check("sem mudanças", PokemonDiff.Compare(pk, pk.Clone()).Count == 0);
var normal = pk.Clone(); normal.MetDate = new DateOnly(2025, 2, 1);
Check("resumo normal", PokemonDiff.Summary(PokemonDiff.Compare(pk, normal)).Contains("importância normal"));
Check("comparação não muda origem", pk.MetDate == new DateOnly(2025, 1, 1));
var unknown = pk.Clone(); unknown.MetLocation = 60000;
var otherUnknown = unknown.Clone(); otherUnknown.MetLocation = 60001;
Check("IDs desconhecidos não escondem mudanças", PokemonDiff.Compare(unknown, otherUnknown).Any(c => c.Field == "Local do encontro"));
Loc.Load(Loc.English);
Check("nomes privados não são traduzidos", new PokemonChange("OT", "Caixas", "Treinador", true).Text.Contains("Caixas → Treinador"));
Check("rótulo desconhecido traduzido", new PokemonChange("Local do encontro", "Desconhecido", "—", false).Text.Contains("Unknown"));
Loc.Load(Loc.Portuguese);
var gen3 = new PK3 { Species = 25, Version = GameVersion.FR, OriginalTrainerName = "DEMO", Language = 2,
    Nickname = "Pikachu", MetLevel = 5, MetLocation = 20, Ball = 4, PID = 0x12345678, CurrentLevel = 10 };
var gen4 = CoreAdapter.ConvertForSave(BlankSaveFile.Get(GameVersion.Pt), gen3, out _);
Check("Gen 3 → 4: formato", gen4 is PK4);
Check("Gen 3 → 4: encontro", gen4 is not null && PokemonDiff.Compare(gen3, gen4).Any(c => c.Field is "Local do encontro" or "Data do encontro" or "Nível do encontro"));

var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "DEMO"; CoreAdapter.Activate(sav);
var good = EncounterDatabase.SearchEncounters(sav, 25, true).Where(e => e.Species == 25)
    .Select(e => EncounterDatabase.ToEntity(sav, e, out _)).First(p => p is not null && new LegalityAnalysis(p).Valid)!;
var bad = good.Clone(); bad.MetLocation = 60000; bad.MetLevel = 0;
Check("legalidade", PokemonDiff.Compare(bad, good).Any(c => c.Field == "Legalidade"));
sav.SetBoxSlotAtIndex(bad, 0, 0);
var path = Path.Combine(work, "Black.sav"); File.WriteAllBytes(path, sav.Write().ToArray());
var vm = new MainViewModel(new AppSettings { CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
vm.Open(path); vm.CurrentPage = vm.Boxes; Pump();
PKM? candidate = null; bool shot = false;
var task = vm.SelectSlotAsync(vm.Boxes.Slots[0]);
Wait(() => task.IsCompleted, () =>
{
    if (vm.Dialog is not { } dialog) return;
    candidate = vm.Editor!.PreviewCandidate;
    Check("pergunta tem comparação", dialog.Details.Any(t => t.Contains("→")));
    if (!shot) { win.UpdateLayout(); for (int i = 0; i < 8; i++) Pump(); win.CaptureRenderedFrame()?.Save(Path.Combine(work, "change-preview.png")); shot = true; }
    dialog.Complete(true);
});
byte[] Stored(PKM p) { var data = new byte[p.SIZE_STORED]; p.WriteDecryptedDataStored(data); return data; }
Check("aplicado é exatamente o candidato", candidate is not null && Stored(vm.Boxes.Slots[0].Pkm!).SequenceEqual(Stored(candidate)));
vm.UndoCommand.Execute(null); Pump();
Check("desfazer recupera ilegal", vm.Boxes.Slots[0].IsLegal == false);
var before = Stored(vm.ActiveTab!.Sav.GetBoxSlotAtIndex(0, 0));
var reopen = vm.SelectSlotAsync(vm.Boxes.Slots[0]);
Wait(() => reopen.IsCompleted, () => vm.Dialog?.Complete(false));
var cancel = vm.Editor!.PreviewLegalizeAsync(false);
Wait(() => cancel.IsCompleted, () => vm.Dialog?.Complete(false));
Check("cancelamento sem edição", before.SequenceEqual(Stored(vm.ActiveTab.Sav.GetBoxSlotAtIndex(0, 0))));
win.Close();
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
