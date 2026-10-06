using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; int fails = 0;
Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
PKM Blank(SaveFile s, ushort sp) { var p = s.BlankPKM; p.Species = sp; p.CurrentLevel = 15; p.Gender = 0; return p; }
PKM Make(SaveFile s, ushort sp)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(s, sp, true).Where(e => e.Species == sp).OrderBy(e => e is EncounterSlot8 ? 0 : 1))
        if (EncounterDatabase.ToEntity(s, enc, out _) is { IsEgg: false } p && new LegalityAnalysis(p).Valid) return p;
    throw new Exception("No encounter " + sp);
}
foreach (var version in new[] { GameVersion.SH, GameVersion.Pt, GameVersion.C })
{
    var s = BlankSaveFile.Get(version); s.OT = "DEMO"; CoreAdapter.Activate(s);
    var eevee = Blank(s, 133); var list = AssistedEvolution.List(eevee, s);
    Check(version + " eeveelutions", list.Select(e => e.Method.Species).Distinct().Count() == (version == GameVersion.C ? 5 : version == GameVersion.Pt ? 7 : 8));
    if (version == GameVersion.Pt) Check("Gen 4 rocks", list.Any(e => e.Requirement.Contains("Moss Rock")) && list.Any(e => e.Requirement.Contains("Ice Rock")));
    var tyrogue = Blank(s, 236); var ty = AssistedEvolution.List(tyrogue, s);
    Check(version + " Tyrogue three branches", ty.Select(e => e.Method.Species).Distinct().Count() == 3);
}
var save = BlankSaveFile.Get(GameVersion.SH); save.OT = "DEMO"; CoreAdapter.Activate(save);
var pancham = AssistedEvolution.List(Blank(save, 674), save).Single();
Check("Pancham requires Dark teammate, not a learned move", pancham.Blocked is null && pancham.Requirement.Contains("Dark na equipe"));
Check("Mantyke requires Remoraid teammate", AssistedEvolution.List(Blank(save, 458), save).Single().Requirement.Contains("Remoraid"));
var sun = BlankSaveFile.Get(GameVersion.US);
var lycanroc = AssistedEvolution.List(Blank(sun, 744), sun);
Check("version-specific Lycanroc keeps day/night requirements", lycanroc.Any(e => e.Requirement.Contains("de dia")) && lycanroc.Any(e => e.Requirement.Contains("à noite")));
CoreAdapter.Activate(save);
var milcery = Make(save, 868); milcery.HeldItem = Array.IndexOf(CoreAdapter.GetItemNames(milcery).ToArray(), "Love Sweet");
var creams = AssistedEvolution.List(milcery, save);
Check("Alcremie flavors have distinct names", creams.Select(e => e.Name).Distinct().Count() == creams.Count && creams.Count > 1);
var alcremie = AssistedEvolution.Build(milcery, save, creams.Single(e => e.Form == 0));
Check("Alcremie consumes Sweet and retains its decoration", alcremie.Species == 869 && alcremie.HeldItem == 0 && alcremie is IFormArgument { FormArgument: (uint)AlcremieDecoration.Love } && new LegalityAnalysis(alcremie).Valid);
var wurmpleSave = BlankSaveFile.Get(GameVersion.E); CoreAdapter.Activate(wurmpleSave);
var wurmple = Blank(wurmpleSave, 265); wurmple.PID = 0;
var w = AssistedEvolution.List(wurmple, wurmpleSave);
Check("Wurmple both branches, one allowed", w.Count == 2 && w.Count(e => e.Blocked is null) == 1);
CoreAdapter.Activate(save);
var kirlia = Blank(save, 281); kirlia.Gender = 1;
Check("female Kirlia Gallade blocked", AssistedEvolution.List(kirlia, save).Single(e => e.Method.Species == 475).Blocked is not null);
kirlia.Gender = 0;
Check("male Kirlia Gallade allowed", AssistedEvolution.List(kirlia, save).Single(e => e.Method.Species == 475).Blocked is null);
var yan = Blank(save, 193); // Yanma is not present in Shield: test Platinum instead.
var platinum = BlankSaveFile.Get(GameVersion.Pt); CoreAdapter.Activate(platinum); yan = Blank(platinum, 193);
Check("Yanmega needs Ancient Power", AssistedEvolution.List(yan, platinum).Single().Blocked is not null);
yan.Move1 = (ushort)Move.AncientPower;
Check("Yanmega ready with move", AssistedEvolution.List(yan, platinum).Single().Ready);
CoreAdapter.Activate(save);
var source = Make(save, 821); source.CurrentLevel = 10;
source.HeldItem = Array.IndexOf(CoreAdapter.GetItemNames(source).ToArray(), "Everstone"); source.RefreshChecksum();
var bytes = source.Data.ToArray(); var opt = AssistedEvolution.List(source, save).Single();
var evolved = AssistedEvolution.Build(source, save, opt);
Check("Everstone removed, level raised, legal", evolved.HeldItem == 0 && evolved.CurrentLevel >= 18 && evolved.Species == 822 && new LegalityAnalysis(evolved).Valid);
Check("preserves source and identifiers", source.Data.SequenceEqual(bytes) && source.PID == evolved.PID && source.EncryptionConstant == evolved.EncryptionConstant && source.IV_HP == evolved.IV_HP);
var egg = source.Clone(); egg.IsEgg = true; Check("egg has no choices", AssistedEvolution.List(egg, save).Count == 0);
bool answer = false; var history = new SlotHistory(save); save.SetBoxSlotAtIndex(source, 0, 0);
var editor = new PokemonEditorViewModel(source, "Teste", p => { history.Record("evolve", [new(0, 0)]); save.SetBoxSlotAtIndex(p, 0, 0); }, Console.WriteLine, sav: save, legalMode: true)
{
    ConfirmPreview = (_, _, _, _, details) => { Check("preview includes Everstone", details.Any(d => d.Contains("Everstone"))); return Task.FromResult(answer); },
};
Check("cancel leaves editor untouched", !await editor.PreviewEvolutionAsync(opt) && !editor.IsModified && editor.SpeciesName == "Rookidee");
answer = true;
Check("confirm edits editor only", await editor.PreviewEvolutionAsync(opt) && editor.IsModified && save.GetBoxSlotAtIndex(0, 0).Species == 821);
editor.ApplyCommand.Execute(null); history.Undo();
Check("Apply then undo", save.GetBoxSlotAtIndex(0, 0).Species == 821);
var bad = source.Clone(); bad.MetLocation = 65535;
var illegal = new PokemonEditorViewModel(bad, "Teste", _ => { }, _ => { }, sav: save, legalMode: true) { ConfirmPreview = (_, _, _, _, _) => Task.FromResult(true) };
Check("legal mode blocks illegal evolution", !await illegal.PreviewEvolutionAsync(AssistedEvolution.List(bad, save).Single()));
illegal.LegalMode = false;
Check("disabled legal mode permits warning", await illegal.PreviewEvolutionAsync(AssistedEvolution.List(bad, save).Single()));

foreach (var (version, species, destination) in new (GameVersion, ushort, ushort)[]
{
    (GameVersion.E, 290, 292), (GameVersion.X, 64, 65), (GameVersion.SH, 133, 134),
    (GameVersion.SH, 133, 700), (GameVersion.SL, 999, 1000), (GameVersion.SH, 562, 867),
})
{
    var s = BlankSaveFile.Get(version); CoreAdapter.Activate(s);
    var p = Make(s, species);
    if (species == 133 && destination == 700) { p.CurrentLevel = Math.Max((byte)20, p.CurrentLevel); p.Move1 = (ushort)Move.BabyDollEyes; p.HealPP(); }
    if (destination == 867) { p.Form = 1; p.MetLocation = 122; } // No valid Galar encounter is implied by this synthetic mutation.
    var choice = AssistedEvolution.List(p, s).FirstOrDefault(e => e.Method.Species == destination);
    if (choice is null) { Check(version + " special choice " + destination, false); continue; }
    if (choice.Blocked is not null) { Console.WriteLine(choice.Blocked); Check(version + " special unblocked " + destination, false); continue; }
    var candidate = AssistedEvolution.Build(p, s, choice);
    Check(version + " special result " + destination, candidate.Species == destination);
    if (destination == 867) Check("Runerigus counter in preview", PokemonDiff.Compare(p, candidate).Any(c => c.Field == "Contador de evolução"));
    else { var la = new LegalityAnalysis(candidate); if (!la.Valid) Console.WriteLine(la.Report()); Check(version + " special legal " + destination, la.Valid); }
    if (destination == 700) Check("friendship in preview", PokemonDiff.Compare(p, candidate).Any(c => c.Field == "Felicidade"));
    if (destination == 65) Check("trade partner preserved or shown in preview", !p.IsUntraded || PokemonDiff.Compare(p, candidate).Any(c => c.Field == "Parceiro de troca"));
}
Loc.Load(Loc.English);
Check("Pancham English requirement", AssistedEvolution.List(Blank(save, 674), save).Single().Requirement.Contains("Dark Pokemon in the party"));
foreach (var v in new[] { GameVersion.SH, GameVersion.Pt, GameVersion.C })
{
    var s = BlankSaveFile.Get(v); CoreAdapter.Activate(s);
    var choices = AssistedEvolution.List(Blank(s, 133), s);
    var portuguese = new System.Text.RegularExpressions.Regex(@"[ãõçâêôáíóúà]|\b(nível|felicidade|golpe|subir|macho|fêmea|remover)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    foreach (var e in choices) if (portuguese.IsMatch(e.Requirement + e.Blocked)) Console.WriteLine("Missing EN: " + e.Requirement + e.Blocked);
    Check(v + " English requirements", choices.All(e => !portuguese.IsMatch(e.Requirement + e.Blocked)));
}
Loc.Load(Loc.Portuguese);
var itemSave = BlankSaveFile.Get(GameVersion.Pt); CoreAdapter.Activate(itemSave);
var sneasel = Make(itemSave, 215);
sneasel.HeldItem = Array.IndexOf(CoreAdapter.GetItemNames(sneasel).ToArray(), "Leftovers");
var heldChoice = AssistedEvolution.List(sneasel, itemSave).Single();
Check("unrelated held item preserved", AssistedEvolution.Build(sneasel, itemSave, heldChoice).HeldItem == sneasel.HeldItem);
sneasel.HeldItem = heldChoice.Method.Argument;
Check("required held item consumed", AssistedEvolution.Build(sneasel, itemSave, heldChoice).HeldItem == 0);

// Synthetic captures of the actual unified editor, desktop and MobileShell.
var captureSave = BlankSaveFile.Get(GameVersion.B); captureSave.OT = "DEMO"; CoreAdapter.Activate(captureSave);
captureSave.SetBoxSlotAtIndex(Make(captureSave, 133), 0, 0);
var path = Path.Combine(work, "Black.sav"); File.WriteAllBytes(path, captureSave.Write().ToArray());
var vm = new MainViewModel(new AppSettings { CheckForUpdates = false }); vm.Open(path); vm.CurrentPage = vm.Boxes; vm.Boxes.SelectSlotCommand.Execute(vm.Boxes.Slots[0]);
void Pump() { for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
var desktop = new PKHeX.Modern.Views.MainWindow { DataContext = vm, Width = 1440, Height = 1000 }; desktop.Show(); Pump(); desktop.CaptureRenderedFrame()?.Save(Path.Combine(work, "evolution-desktop.png")); desktop.Hide();
App.ShowShortcuts = false;
var phone = new Avalonia.Controls.Window { Width = 892, Height = 412, Content = new PKHeX.Modern.Views.MobileShell(vm) }; phone.Show(); Pump();
var heading = phone.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "EVOLUIR");
heading.GetVisualAncestors().OfType<ScrollViewer>().First().Offset = new Vector(0, 500); Pump();
phone.CaptureRenderedFrame()?.Save(Path.Combine(work, "evolution-mobile.png")); phone.Close();
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
