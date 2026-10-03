using System; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Diagnostics; using Avalonia; using Avalonia.Headless; using Avalonia.Threading; using Avalonia.Input; using Avalonia.VisualTree; using Avalonia.Controls;
using PKHeX.Core; using PKHeX.Modern; using PKHeX.Modern.Services; using PKHeX.Modern.ViewModels; using PKHeX.Modern.Views;
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0]; int fails = 0;
SaveBackup.Folder = Path.Combine(dir, "backups");
BankStorage.Root = Path.Combine(dir, "bank");
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {n} {extra}"); if (!ok) fails++; }

// prepara as copias: Vulpix e Meowstic no Shield (caixa 1, slots 1-2), Deoxys no Emerald (caixa 1, slot 1)
PKM Make(SaveFile sav, ushort sp, byte form, byte gender)
{
    var enc = EncounterDatabase.SearchEncounters(sav, sp, true).First(e => e.Form == form);
    var pk = EncounterDatabase.ToEntity(sav, enc, EncounterCriteria.Unrestricted, out _)!;
    return pk;
}
var sh = CoreAdapter.LoadSave(Path.Combine(dir, "sh.sav"))!; CoreAdapter.Activate(sh);
sh.SetBoxSlotAtIndex(Make(sh, 37, 0, 0), 0, 0);
var meow = Make(sh, 678, 0, 0); meow.Species = 678; meow.Form = 0; meow.Gender = 0; meow.ClearNickname(); meow.RefreshAbility(0); meow.CurrentLevel = 30; meow.ResetPartyStats();
Console.WriteLine($"Meowstic preparado: legal {new LegalityAnalysis(meow).Valid}");
sh.SetBoxSlotAtIndex(meow, 0, 1);
File.WriteAllBytes(Path.Combine(dir, "sh.sav"), sh.Write().ToArray());
foreach (var i in new[] { 0, 1 }) { var x = sh.GetBoxSlotAtIndex(0, i); Console.WriteLine($"slot {i}: {GameInfo.Strings.specieslist[x.Species]} form {x.Form} gender {x.Gender} legal {new LegalityAnalysis(x).Valid} dual {x.PersonalInfo.IsDualGender} formCount {x.PersonalInfo.FormCount}"); }
Console.WriteLine("Meowstic forms SWSH: " + string.Join(",", Enumerable.Range(0, 2).Select(f => sh.Personal.IsPresentInGame(678, (byte)f))));
var em = CoreAdapter.LoadSave(Path.Combine(dir, "em.sav"))!; CoreAdapter.Activate(em);
var deoxys = em.BlankPKM; deoxys.Species = 386; deoxys.CurrentLevel = 30; em.SetBoxSlotAtIndex(deoxys, 0, 0);
File.WriteAllBytes(Path.Combine(dir, "em.sav"), em.Write().ToArray());

var vm = new MainViewModel();
var win = new MainWindow { DataContext = vm, Width = 1500, Height = 950 }; win.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Wait(Func<bool> done, int ms = 60000) { var sw = Stopwatch.StartNew(); while (!done() && sw.ElapsedMilliseconds < ms) { Pump(); if (vm.Dialog is { } d) d.Complete(true); System.Threading.Thread.Sleep(10); } Pump(); }
void Select(int box, int slot) { vm.CurrentPage = vm.Boxes; vm.Boxes.CurrentBox = box; Pump(); var t = vm.SelectSlotAsync(vm.Boxes.Slots[slot]); Wait(() => t.IsCompleted); }
var o = vm.OpenAsync(Path.Combine(dir, "sh.sav")); Wait(() => o.IsCompleted);

// Vulpix: formas com sprite
Select(0, 0);
var ed = vm.Editor!;
Check("Vulpix tem formas com sprite", ed.HasForms && ed.FormOptions.All(f => f.Sprite is not null), string.Join(", ", ed.FormOptions.Select(f => f.Text)));
var combo = win.GetVisualDescendants().OfType<ComboBox>().First(c => c.ItemsSource == ed.FormOptions);
combo.IsDropDownOpen = true; Pump(); Pump();
win.CaptureRenderedFrame()!.Save(Path.Combine(dir, "forms_open.png"));
foreach (var tl in TopLevel.GetTopLevel(combo)!.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>()) { }
combo.IsDropDownOpen = false; Pump();

// Meowstic: genero e forma juntos (modo legal desligado e ligado)
Select(0, 1);
ed = vm.Editor!;
Console.WriteLine($"Meowstic: genero {(int)ed.GenderSymbol[0]} forma {ed.SelectedForm?.Text} podeTrocar={ed.CanToggleGender}");
vm.LegalMode = false; Pump();
ed.ToggleGender(); Pump();
Check("Meowstic (sem modo legal): genero e forma trocam juntos", ed.GenderSymbol == "♀" && ed.SelectedForm?.Value == 1, $"{ed.GenderSymbol} {ed.SelectedForm?.Text}");
vm.LegalMode = true; Select(0, 1); ed = vm.Editor!;
ed.ToggleGender(); Wait(() => !ed.IsLegalizing);
Check("Meowstic (modo legal): genero/forma trocados e legal", ed.GenderSymbol == "♀" && ed.SelectedForm?.Value == 1 && ed.IsLegal, $"{ed.GenderSymbol} {ed.SelectedForm?.Text} | {vm.Status}");
ed.SelectedForm = ed.FormOptions.First(f => f.Value == 0); Wait(() => !ed.IsLegalizing);
Check("Meowstic: trocar a forma troca o genero", ed.GenderSymbol == "♂" && ed.IsLegal, $"{ed.GenderSymbol} | {vm.Status}");

// Gen 3: genero pelo PID (Emerald), Deoxys
o = vm.OpenAsync(Path.Combine(dir, "em.sav")); Wait(() => o.IsCompleted);
Select(0, 0); ed = vm.Editor!;
Check("Deoxys Gen 3: aviso da forma", ed.HasFormNote && !ed.HasForms, ed.FormNote);
o = vm.OpenAsync(Path.Combine(dir, "sa.sav")); Wait(() => o.IsCompleted);
var sav3 = (SaveFile)typeof(MainViewModel).GetField("_sav", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
var all3 = EntitySearch.ReadAll(sav3).Where(x => x.Box >= 0 && x.Pkm.Species != 0 && !x.Pkm.IsEgg).ToList();
Console.WriteLine($"Emerald: {all3.Count} nas caixas, {all3.Count(x => x.Pkm.PersonalInfo.IsDualGender)} com dois generos, {all3.Count(x => new LegalityAnalysis(x.Pkm).Valid)} legais");
var dual = all3.Where(x => x.Pkm.PersonalInfo.IsDualGender && new LegalityAnalysis(x.Pkm).Valid).First();
Select(dual.Box, dual.Slot); ed = vm.Editor!;
var g0 = ed.GenderSymbol; var nat = ed.Nature; Console.WriteLine($"   {ed.SpeciesName} legal antes: {ed.IsLegal}");
ed.ToggleGender(); Wait(() => !ed.IsLegalizing);
Console.WriteLine($"   legal depois: {ed.IsLegal}");
Check("Gen 3: troca de genero (PID novo, mesma natureza, legal)", ed.GenderSymbol != g0 && ed.Nature == nat && ed.IsLegal, $"{ed.SpeciesName} {g0}->{ed.GenderSymbol} | {vm.Status}");
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
