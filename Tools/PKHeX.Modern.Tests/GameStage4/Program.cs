using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;
using static System.Buffers.Binary.BinaryPrimitives;

// Editores por jogo, etapa 4: Battle Frontier (Emerald, HeartGold, Platinum/Diamond sinteticos), Pokétch (sintetico),
// Pokéwalker e Pokéathlon (HeartGold), base secreta (Emerald e Omega Ruby). Copias dos saves reais pelo run-tests.ps1;
// grava, reabre e confere; Ctrl+Z desfaz.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-stage4-" + Guid.NewGuid()); Directory.CreateDirectory(work);
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
var g = vm.Game;

// ---------- Emerald: Battle Frontier da Gen 3 e base secreta ----------
vm.Open(Path.Combine(work, "em.sav")); Pump();
vm.CurrentPage = g; g.Tab = 11; Pump();
var f = g.Frontier!;
Check("[Emerald] aba Battle Frontier", g.HasFrontier && f.IsGen3 && f.Facilities.Count == 7 && f.Symbols.Count == 7);
f.BP = 1234; f.FrontierPass = true; f.Symbols[0].State = 2;
f.FacilityIndex = 4; f.ModeIndex = 1; f.LevelIndex = 1; Pump(); // Battle Factory, Doubles, Open Level
Check("[Emerald] Factory tem 4 números", f.Stats.Count == 4, string.Join(", ", f.Stats.Select(s => s.Label)));
f.Stats[0].Value = 21; f.Stats[2].Value = 35; f.Continue = true;
Shot("frontier3");
var before = g.History!.Count;
g.UndoGame(); Pump(); f = g.Frontier!;
f.FacilityIndex = 4; f.ModeIndex = 1; f.LevelIndex = 1;
Check("[Emerald] Ctrl+Z desfaz a última mudança (série em andamento)", !f.Continue && f.Stats[0].Value == 21, $"{before} passos");
g.RedoGame(); Pump();
g.Tab = 14; Pump();
var bases3 = g.SecretBase3!;
Console.WriteLine($"     bases secretas no Emerald: {bases3.Bases.Count}");
if (bases3.Bases.Count > 0) { bases3.Selected = bases3.Bases[0]; bases3.Bases[0].TimesEntered = 9; Shot("secretbase3"); }
var em = (SAV3E)Reopen("em.sav");
var bf3 = em.SmallBlock.BattleFrontier;
Check("[Emerald] BP, Frontier Pass e símbolo de ouro gravados", em.SmallBlock.BP == 1234 && em.GetEventFlag(BattleFrontier3.FrontierPassFlagIndex)
    && em.GetEventFlag(BattleFrontier3.GetSymbolGoldFlagIndex(BattleFrontierFacility3.Tower)), $"BP {em.SmallBlock.BP}");
Check("[Emerald] sequência da Factory gravada", bf3.GetStat(BattleFrontierFacility3.Factory, BattleFrontierBattleMode3.Doubles, BattleFrontierRecordType3.OpenLevel, BattleFrontierStatType3.CurrentStreak) == 21
    && bf3.GetStat(BattleFrontierFacility3.Factory, BattleFrontierBattleMode3.Doubles, BattleFrontierRecordType3.OpenLevel, BattleFrontierStatType3.RecordStreak) == 35
    && bf3.GetContinueFlag(BattleFrontierFacility3.Factory, BattleFrontierBattleMode3.Doubles, BattleFrontierRecordType3.OpenLevel));
if (bases3.Bases.Count > 0)
    Check("[Emerald] visitas da base gravadas", ((ISaveBlock3LargeHoenn)em.LargeBlock).SecretBases.Bases[0].TimesEntered == 9);

// ---------- Ruby/Sapphire: bases secretas da Gen 3 ----------
// Nenhum save real de teste tem base secreta: cria uma (de outro jogador, com equipe) numa copia do Emerald.
{
    var src = (SAV3E)CoreAdapter.LoadSave(Path.Combine(work, "em.sav"))!;
    var raw = new byte[SecretBase3.SIZE];
    var made = new SecretBase3(raw) { SecretBaseLocation = 12, TID16 = 4321, SID16 = 8765, TimesEntered = 2 };
    made.Language = src.Language;
    made.OriginalTrainerName = "MAY";
    var team = made.Team; team.Team[0].Species = 286; team.Team[0].Level = 30; team.Team[0].Move1 = 73; made.Team = team;
    raw.CopyTo(src.Large[0x1A9C..]);
    File.WriteAllBytes(Path.Combine(work, "embase.sav"), src.Write().ToArray());
}
foreach (var file in new[] { "embase.sav", "ru.sav", "sa.sav" })
{
    vm.Open(Path.Combine(work, file)); Pump();
    vm.CurrentPage = g; g.Tab = 14; Pump();
    var sb = g.SecretBase3!;
    Console.WriteLine($"     [{file}] {sb.Summary} {string.Join(" | ", sb.Bases.Select(b => b.Title + " " + b.Team.Count))}");
    if (sb.Bases.Count == 0) continue;
    sb.Selected = sb.Bases[^1]; sb.Bases[^1].TimesEntered = 7; sb.Bases[^1].Registered = true;
    Shot("secretbase3-" + file);
    var r = (SAV3)Reopen(file);
    var saved = ((ISaveBlock3LargeHoenn)r.LargeBlock).SecretBases.Bases[^1];
    Check($"[{file}] base secreta gravada", saved.TimesEntered == 7 && saved.RegistryStatus == 1);
}

// ---------- HeartGold: Battle Frontier da Gen 4, Pokéwalker e Pokéathlon ----------
vm.Open(Path.Combine(work, "hg.sav")); Pump();
vm.CurrentPage = g; g.Tab = 11; Pump();
f = g.Frontier!;
Check("[HeartGold] Battle Frontier com 5 instalações e impressões", f.IsGen4 && f.Facilities.Count == 5 && f.Prints.Count == 5);
f.BP = 500; f.Prints[0].State = 4;
f.FacilityIndex = 1; f.ModeIndex = 1; f.LevelIndex = 1; Pump(); // Battle Factory, Doubles, Open Level
Check("[HeartGold] Factory: atual, trocas, recorde e trocas recorde", f.Stats.Count == 4 && f.HasLevels, string.Join(", ", f.Stats.Select(s => s.Label)));
f.Stats[0].Value = 14; f.Stats[2].Value = 28;
f.FacilityIndex = 3; Pump();
Check("[HeartGold] Battle Castle mostra CP", f.Stats.Any(s => s.Label.Contains("CP")) && !f.HasLevels);
Shot("frontier4");
g.Tab = 12; Pump();
var walker = g.Pokewalker!;
Check("[HeartGold] aba Pokéwalker", g.HasGadget && g.GadgetTabText.Contains("Pokéwalker") && g.Poketch is null && walker.Courses.Count > 20);
walker.Watts = 777; walker.UnlockAllCommand.Execute(null);
var unlocked = walker.Courses.Count(c => c.Unlocked);
Check("[HeartGold] rotas liberadas", unlocked >= 23, walker.Summary);
g.Tab = 13; Pump();
var medals = g.Pokeathlon!;
medals.Query = "pikachu"; Pump();
Check("[HeartGold] busca no Pokéathlon", medals.Rows.Count == 1 && medals.Rows[0].Name == "Pikachu");
medals.Rows[0].Speed = true; medals.Rows[0].Jump = true;
medals.Query = "#001"; Pump();
medals.Rows[0].All = true;
medals.Query = ""; Pump();
Shot("pokeathlon");
var hg = (SAV4HGSS)Reopen("hg.sav");
Check("[HeartGold] BP gravados", hg.BP == 500, $"BP {hg.BP}");
var g2 = new GamePageViewModel((_, _, _) => Task.FromResult(true), _ => { }); g2.Load(hg);
var f2 = g2.Frontier!; f2.FacilityIndex = 1; f2.ModeIndex = 1; f2.LevelIndex = 1;
Check("[HeartGold] sequência da Factory relida", f2.Stats[0].Value == 14 && f2.Stats[2].Value == 28, string.Join("/", f2.Stats.Select(s => s.Value)));
f2.LevelIndex = 0;
Check("[HeartGold] Level 50 ficou separado do Open Level", f2.Stats[0].Value != 14 || f2.Stats[2].Value != 28);
Check("[HeartGold] impressão da Tower de ouro", f2.Prints[0].State == 4);
Check("[HeartGold] watts e rotas gravados", hg.PokewalkerWatts == 777 && g2.Pokewalker!.Courses.Count(c => c.Unlocked) == unlocked);
Check("[HeartGold] medalhas gravadas", hg.Pokeathlon.Medals.GetMedal(25) == 0b10001 && hg.Pokeathlon.Medals.GetMedal(1) == 0b11111,
    $"Pikachu {hg.Pokeathlon.Medals.GetMedal(25)}, Bulbasaur {hg.Pokeathlon.Medals.GetMedal(1)}");

// ---------- Omega Ruby: base secreta ----------
vm.Open(Path.Combine(work, "or.sav")); Pump();
vm.CurrentPage = g; g.Tab = 14; Pump();
var b6 = g.SecretBase6!;
Check("[Omega Ruby] aba Base secreta", g.HasSecretBase && g.SecretBase3 is null);
b6.TeamName = "Equipe Teste"; b6.Rank = 3; b6.FlagsFromFriends = 42; b6.GiveAllGoodsCommand.Execute(null);
var others = b6.Others.Count;
if (others > 0) { b6.Others[0].DeleteCommand.Execute(null); Wait(() => g.SecretBase6!.Others.Count < others || b6.Others.Count < others, 3000); }
Shot("secretbase6");
var or = (SAV6AO)Reopen("or.sav");
var self = or.SecretBase.GetSecretBaseSelf();
Check("[Omega Ruby] textos e números da base gravados", self.TeamName == "Equipe Teste" && self.Rank == SecretBase6Rank.Gold && self.TotalFlagsFromFriends == 42, self.TeamName);
Check("[Omega Ruby] decorações no estoque", or.SecretBase.GetGood(0).Count > 0);
if (others > 0)
    Check("[Omega Ruby] base de outro jogador apagada", Enumerable.Range(0, SecretBase6Block.OtherSecretBaseCount).Count(i => !or.SecretBase.GetSecretBaseOther(i).IsEmpty) == others - 1);

// ---------- Platinum e Diamond sinteticos (em memoria: o Core nao grava saves vazios da Gen 4) ----------
var pt = (SAV4Pt)BlankSaveFile.Get(GameVersion.Pt);
var gp = new GamePageViewModel((_, _, _) => Task.FromResult(true), _ => { }); gp.Load(pt);
var poketch = gp.Poketch!;
Check("[Platinum] aba Pokétch", gp.HasGadget && gp.GadgetTabText.Contains("Pokétch") && gp.Pokewalker is null && poketch.Apps.Count == (int)PoketchApp.Alarm_Clock + 1);
poketch.UnlockAllCommand.Execute(null); poketch.CurrentApp = 5;
Check("[Platinum] todos os aplicativos liberados e contados", Enumerable.Range(0, poketch.Apps.Count).All(i => pt.GetPoketchAppUnlocked((PoketchApp)i))
    && pt.PoketchUnlockedCount == poketch.Apps.Count && pt.CurrentPoketchApp == 5);
gp.UndoGame();
Check("[Platinum] desfazer volta o aplicativo da tela", pt.CurrentPoketchApp != 5);
var fp = gp.Frontier!;
fp.Stats[0].Value = 49; // Battle Tower, Singles: 49 vitorias = 7 series
Check("[Platinum] Battle Tower grava também as séries de 7", ReadUInt16LittleEndian(pt.General[(0x723D + 1)..]) == 7 && ReadUInt16LittleEndian(pt.General[(0x68E0 + 2)..]) == 49);
var dp = BlankSaveFile.Get(GameVersion.D);
var gd = new GamePageViewModel((_, _, _) => Task.FromResult(true), _ => { }); gd.Load(dp);
Check("[Diamond] só a Battle Tower e sem impressões", gd.Frontier!.Facilities.Count == 1 && !gd.Frontier.HasPrints && gd.Poketch is not null && !gd.HasPokeathlon);

Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
