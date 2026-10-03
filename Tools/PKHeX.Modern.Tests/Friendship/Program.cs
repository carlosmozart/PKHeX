using System;
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
int fails = 0;
void Check(string n, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {n}"); if (!ok) fails++; }
PKM Make(SaveFile sav, ushort species)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, species, true).Where(e => e.Species == species))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk && new LegalityAnalysis(pk).Valid) return pk;
    throw new Exception("Não gerou " + species + " em " + sav.Version);
}
foreach (var version in new[] { GameVersion.C, GameVersion.E, GameVersion.HG, GameVersion.B, GameVersion.X, GameVersion.US, GameVersion.SH, GameVersion.BD, GameVersion.PLA, GameVersion.SL })
{
    var sav = BlankSaveFile.Get(version); CoreAdapter.Activate(sav);
    ushort species = version is GameVersion.SL ? (ushort)172 : (ushort)42;
    var pk = Make(sav, species); pk.CurrentFriendship = 255;
    PKM? applied = null;
    var editor = new PokemonEditorViewModel(pk, "Teste", p => applied = p, Console.WriteLine, sav: sav, legalMode: true);
    Console.WriteLine(version + ": " + string.Join(" | ", EvolutionTree.GetEvolutionTree(pk.Context).Forward.GetForward(pk.Species, pk.Form).ToArray().Select(m => m + " " + m.Method + " present=" + sav.Personal.IsPresentInGame(m.Species, m.GetDestinationForm(pk.Form)))));
    if (!editor.HasFriendshipEvolutions) { Check(version + " opções disponíveis", false); continue; }
    var option = editor.FriendshipEvolutions.First();
    int level = pk.CurrentLevel;
    option.Command.Execute(null);
    editor.ApplyCommand.Execute(null);
    Check(version + " evolução legal", applied is not null && applied.Species != pk.Species && new LegalityAnalysis(applied).Valid);
    Check(version + " nível correto", applied is not null && applied.CurrentLevel == (version == GameVersion.PLA ? level : level + 1));
    pk.CurrentFriendship = 0;
    var raised = pk.Clone();
    CoreAdapter.EvolveByFriendship(raised, CoreAdapter.GetFriendshipEvolutions(raised, sav).First(), sav);
    Check(version + " ajusta felicidade mínima", raised.CurrentFriendship == (pk.Format >= 8 ? 160 : 220));
    pk.IsEgg = true;
    Check(version + " ovo sem evolução", CoreAdapter.GetFriendshipEvolutions(pk, sav).Count == 0);
}
var shield = BlankSaveFile.Get(GameVersion.SH); CoreAdapter.Activate(shield);
var eevee = Make(shield, 133); eevee.CurrentFriendship = 160;
eevee.Move1 = 608;
var choices = CoreAdapter.GetFriendshipEvolutions(eevee, shield);
Console.WriteLine(string.Join(" | ", choices.Select(e => e.Name + ": " + e.Requirement + " / " + e.Blocked)));
Check("Sylveon com Fairy tem prioridade", choices.Any(e => e.Species == 700 && e.Blocked is null) && choices.Where(e => e.Species is 196 or 197).All(e => e.Blocked is not null));
eevee.Move1 = 33; eevee.Move2 = eevee.Move3 = eevee.Move4 = 0;
Check("dia/noite disponíveis sem Fairy", CoreAdapter.GetFriendshipEvolutions(eevee, shield).Where(e => e.Species is 196 or 197).All(e => e.Blocked is null));
eevee.HeldItem = 229;
Check("Everstone não bloqueia (o botão tira)", CoreAdapter.GetFriendshipEvolutions(eevee, shield).Where(e => e.Species is 196 or 197).All(e => e.Blocked is null));
{ var ev = eevee.Clone(); CoreAdapter.EvolveByFriendship(ev, CoreAdapter.GetFriendshipEvolutions(ev, shield).First(e => e.Blocked is null), shield);
  Check("Everstone retirada ao evoluir", ev.Species != 133 && ev.HeldItem == 0); }
eevee.HeldItem = 0; eevee.CurrentLevel = 100;
Check("Gen8 admite Rare Candy no nível 100", CoreAdapter.GetFriendshipEvolutions(eevee, shield).Any(e => e.Blocked is null && e.Requirement.Contains("Rare Candy")));
var x = BlankSaveFile.Get(GameVersion.X); CoreAdapter.Activate(x);
var oldEevee = Make(x, 133); oldEevee.CurrentFriendship = 255; oldEevee.Move1 = 608;
((IAffection)oldEevee).OriginalTrainerAffection = 0;
Check("Sylveon antigo sem carinho não bloqueia", CoreAdapter.GetFriendshipEvolutions(oldEevee, x).First(e => e.Species == 700).Blocked is null);
{ var sy = oldEevee.Clone(); sy.CurrentLevel = 20; foreach (ushort mv in Enumerable.Range(1, 700).Select(i => (ushort)i).Where(i => MoveInfo.GetType(i, sy.Context) == 17)) { sy.Move1 = mv; sy.Move1_PP = sy.GetMovePP(mv, 0); if (new LegalityAnalysis(sy).Valid) break; } Console.WriteLine("Fairy: " + sy.Move1 + " legal=" + new LegalityAnalysis(sy).Valid); CoreAdapter.EvolveByFriendship(sy, CoreAdapter.GetFriendshipEvolutions(sy, x).First(e => e.Species == 700), x);
  var la = new LegalityAnalysis(sy); if (!la.Valid) Console.WriteLine(la.Report());
  Console.WriteLine($"sy {sy.Species} h={sy.CurrentHandler} ot={((IAffection)sy).OriginalTrainerAffection} ht={((IAffection)sy).HandlingTrainerAffection} valid={la.Valid}");
  Check("botão sobe o carinho e evolui Sylveon legal", sy.Species == 700 && (sy.CurrentHandler == 0 ? ((IAffection)sy).OriginalTrainerAffection : ((IAffection)sy).HandlingTrainerAffection) >= 50 && la.Valid); }
((IAffection)oldEevee).OriginalTrainerAffection = 50;
((IAffection)oldEevee).HandlingTrainerAffection = 50;
Check("Sylveon antigo aceita dois corações", CoreAdapter.GetFriendshipEvolutions(oldEevee, x).First(e => e.Species == 700).Blocked is null);
oldEevee.CurrentLevel = 100;
Check("Gen6 bloqueia nível 100", CoreAdapter.GetFriendshipEvolutions(oldEevee, x).All(e => e.Blocked is not null));
oldEevee.CurrentLevel = 30; oldEevee.CurrentFriendship = 0; oldEevee.Move1 = 165;
((IAffection)oldEevee).OriginalTrainerAffection = ((IAffection)oldEevee).HandlingTrainerAffection = 0;
var blockedEditor = new PokemonEditorViewModel(oldEevee, "Teste", _ => { }, Console.WriteLine, sav: x, legalMode: true);
blockedEditor.FriendshipEvolutions.First(e => e.Label.Contains("Espeon")).Command.Execute(null);
Check("evolução ilegal mantém edição original", blockedEditor.SpeciesName == "Eevee" && blockedEditor.Friendship == 0 && !blockedEditor.IsModified);
var black = BlankSaveFile.Get(GameVersion.B); CoreAdapter.Activate(black);
var visualEevee = Make(black, 133); visualEevee.CurrentFriendship = 0;
black.SetBoxSlotAtIndex(visualEevee, 0, 0);
var visualPath = Path.Combine(args[0], "black.sav"); File.WriteAllBytes(visualPath, black.Write().ToArray());
var main = new MainViewModel(); main.Open(visualPath);
main.SelectSlotAsync(main.Boxes.Slots[0]).GetAwaiter().GetResult();
var win = new MainWindow { DataContext = main, Width = 1500, Height = 1100 }; win.Show();
for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
win.CaptureRenderedFrame()!.Save(Path.Combine(args[0], "friendship.png"));
Console.WriteLine("Captura: " + args[0]);
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
