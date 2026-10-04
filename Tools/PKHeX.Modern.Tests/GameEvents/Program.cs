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

// Pagina Jogo no Scarlet/Violet (blocos com nome), BD/SP (flags, flags de sistema, valores e atalhos), Gen 1 (atalhos dos
// Pokemon fixos), Let's Go (lista gg e titulos) e Z-A (tabelas de hash, itens do mapa e atalhos), gravando e relendo.
// Scarlet, Red, Yellow e Z-A: COPIAS dos saves reais (run-tests.ps1). BD/SP e Let's Go: saves sinteticos.
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

// ---------- Hall da Fama (copias de Red, Emerald, FireRed, Omega Ruby e Moon; abre antes de qualquer alteracao) ----------
int fameTotal = 0;
foreach (var file in new[] { "red.sav", "em.sav", "fr.sav", "or.sav", "moon.sav" })
{
    var path = Path.Combine(work, file);
    if (!File.Exists(path)) { Console.WriteLine($"(sem {file}: pulado)"); continue; }
    var fame = HallOfFame.Load(CoreAdapter.LoadSave(path)!);
    fameTotal += fame.Count;
    var first = fame.FirstOrDefault();
    Console.WriteLine($"     [{file}] Hall da Fama: {fame.Count} equipes; primeira: {(first is null ? "-" : string.Join(", ", first.Members.Select(m => $"{m.Nickname} Nv.{m.Level}")))} {first?.Date}");
    Check($"[{file}] equipes do Hall da Fama com 1 a 6 Pokémon válidos", fame.All(t => t.Members.Count is >= 1 and <= 6 && t.Members.All(m => m.Species is > 0 and <= 1025)));
    if (fame.Count == 0)
    {
        // Hall da Fama vazio (FireRed sem a Liga): a aba aparece para registrar a equipe atual.
        vm.Open(path); Pump();
        vm.CurrentPage = vm.Game; vm.Game.Tab = 4; Pump();
        Check($"[{file}] aba do Hall da Fama vazia", vm.Game.HasFame && !vm.Game.HasFameTeams && !vm.Game.HasNoRows);
        Shot($"fame-empty-{Path.GetFileNameWithoutExtension(file)}");
        vm.Game.RegisterFameCommand.Execute(null); Pump();
        Check($"[{file}] primeira equipe registrada", HallOfFame.Load(Reopen(file)).Count == 1 && vm.Game.FameRows.Count == 1);
        continue;
    }
    vm.Open(path); Pump();
    var gf = vm.Game;
    vm.CurrentPage = gf; Pump();
    Check($"[{file}] aba Hall da Fama", gf.HasFame && gf.Summary.Contains("Hall da Fama"), gf.Summary);
    gf.Tab = 4; Pump();
    Check($"[{file}] equipes na tela com sprites", gf.FameRows.Count == fame.Count && gf.FameRows[0].Members.All(m => m.Sprite is not null));
    Shot($"fame-{Path.GetFileNameWithoutExtension(file)}");

    // Edicao: troca o primeiro Pokemon da primeira equipe por Mew Nv. 50, grava e rele.
    var original = CoreAdapter.LoadSave(path)!;
    var caps = HallOfFame.Caps(original);
    var target = gf.FameRows[0].Members[0];
    target.SelectCommand.Execute(null); Pump();
    Check($"[{file}] editor do Pokémon clicado", gf.HasFameSelection && gf.FameSpecies == GameInfo.Strings.Species[target.Member.Species]);
    gf.FameSpecies = "Mew"; gf.FameNickname = "TESTE"; gf.FameLevel = 50;
    gf.ApplyFameCommand.Execute(null); Pump();
    Check($"[{file}] edição marca o save como alterado", vm.IsDirty);
    var reread = HallOfFame.Load(Reopen(file));
    var edited = reread.First(t => t.Index == target.TeamIndex).Members.First(m => m.Slot == target.Member.Slot);
    Check($"[{file}] Hall da Fama editado e relido", edited.Species == 151 && (caps.NicknameLength == 0 || (edited.Nickname == "TESTE" && edited.Level == 50)),
        $"{edited.Species} {edited.Nickname} Nv.{edited.Level}");
    Check($"[{file}] seleção continua no Pokémon editado", gf.SelectedFameMember?.Member.Species == 151);

    // Registrar a equipe atual (Moon: vira a equipe mais recente).
    int before = gf.FameRows.Count;
    var partySpecies = Enumerable.Range(0, original.PartyCount).Select(original.GetPartySlotAtIndex).Where(p => p.Species != 0 && !p.IsEgg).Select(p => p.Species).ToArray();
    gf.RegisterFameCommand.Execute(null); Pump();
    reread = HallOfFame.Load(Reopen(file));
    // A vitoria nova entra no fim (Gen 6: primeira posicao livre, ou a ultima quando cheio); no Moon e a equipe mais recente.
    var registered = file == "moon.sav" ? reread.First(t => t.Index == 1) : reread.MaxBy(t => t.Index)!;
    Check($"[{file}] equipe atual registrada", registered.Members.Select(m => m.Species).SequenceEqual(partySpecies)
        && (file == "moon.sav" || reread.Count == Math.Min(before + 1, file.StartsWith("or") ? 16 : 50)), $"{reread.Count} equipes");
    if (caps.HasDate)
        Check($"[{file}] vitória nova com a data de hoje", registered.When == DateTime.Today, registered.Date);

    // Data (X/Y e Omega Ruby/Alpha Sapphire).
    if (caps.HasDate)
    {
        var team = gf.FameRows[0];
        team.EditDate = new DateTime(2016, 2, 27); Pump();
        reread = HallOfFame.Load(Reopen(file));
        Check($"[{file}] data da vitória editada", reread.First(t => t.Index == team.Index).When == new DateTime(2016, 2, 27));
    }

    // Apagar a ultima equipe.
    int count = gf.FameRows.Count;
    var last = gf.FameRows[^1];
    last.DeleteCommand.Execute(null);
    Check($"[{file}] apagar equipe", Wait(() => gf.FameRows.Count == count - 1), $"{gf.FameRows.Count}");
    Check($"[{file}] apagada no save", HallOfFame.Load(Reopen(file)).Count == count - 1);
}
Check("Hall da Fama: algum save com equipes", fameTotal > 0, fameTotal.ToString());

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
// ---------- Gen 1 (copias do Red e do Yellow) ----------
foreach (var file in new[] { "red.sav", "yw.sav" })
{
    var path = Path.Combine(work, file);
    if (!File.Exists(path)) { Console.WriteLine($"(sem {file}: pulado)"); continue; }
    vm.Open(path); Pump();
    var g1 = vm.Game;
    Check($"[{file}] página Jogo com flags e atalhos", vm.Pages.Contains(g1) && g1.HasEvents && g1.HasShortcuts, g1.Summary);
    vm.CurrentPage = g1; Pump();
    Check($"[{file}] flags dos Pokémon fixos com nome", g1.FlagRows.Any(f => f.Name.StartsWith("Mewtwo")), g1.CountText);
    g1.Tab = 3; Pump();
    Check($"[{file}] atalhos de lendários e presentes", g1.ShortcutRows.Any(r => r.Name.StartsWith("Mewtwo")) && g1.ShortcutRows.Any(r => r.Name.StartsWith("Voltorb")),
        string.Join(", ", g1.ShortcutRows.Select(r => r.Name)));
    // Some com o Mewtwo pelo Core e confere que o atalho o traz de volta
    var s1 = (SAV1)vm.ActiveTab!.Sav;
    var sp = new G1OverworldSpawner(s1);
    foreach (var p in sp.GetFlagPairs().Where(p => p.Name == "FlagMewtwo")) p.SetState(true);
    sp.Save();
    var mew = g1.ShortcutRows.First(r => r.Name.StartsWith("Mewtwo"));
    mew.Refresh(); Pump();
    Check($"[{file}] atalho do Mewtwo disponível depois de capturado", mew.IsReady);
    mew.ApplyCommand.Execute(null);
    Check($"[{file}] Mewtwo de volta (pergunta respondida)", Wait(() => !mew.IsReady), mew.State);
    if (file == "red.sav") Shot("gen1-shortcuts");
    var r1 = (SAV1)Reopen(file);
    Check($"[{file}] Mewtwo de volta gravado", new G1OverworldSpawner(r1).GetFlagPairs().First(p => p.Name == "FlagMewtwo").IsHidden == false);
}

// ---------- Let's Go (sintetico) ----------
// O PKHeX nao reconhece um save de Let's Go em branco como arquivo (falta o rodape do jogo): testa a pagina direto.
var gg0 = (SAV7b)BlankSaveFile.Get(GameVersion.GP); gg0.OT = "Demo";
var gg = new GamePageViewModel((_, _, _) => System.Threading.Tasks.Task.FromResult(true), _ => { });
gg.Load(gg0);
Check("[LGPE] página Jogo com flags, valores e atalhos", gg.IsAvailable && gg.HasEvents && gg.HasWorks && gg.HasShortcuts, gg.Summary);
Check("[LGPE] nomes da lista gg (Snorlax, Articuno)", gg.FlagRows.Any(f => f.Name.Contains("Snorlax")) && gg.FlagRows.Any(f => f.Name.Contains("Articuno")), gg.CountText);
var articuno = gg.FlagRows.First(f => f.Name.Contains("Articuno"));
Check("[LGPE] código por tipo (V#0500 = objeto 500)", articuno.Number == "V#0500" && articuno.Category == "Objetos do mapa", $"{articuno.Number} {articuno.Category}");
articuno.IsSet = true;
gg.ShowUnnamed = true;
Check("[LGPE] 4096 flags", gg.FlagRows.Count == 4096, gg.CountText);
gg.ShowUnnamed = false; gg.Tab = 1;
var ggWork = gg.WorkRows.First();
var ggWorkName = ggWork.Name;
ggWork.ValueNumber = 3;
Check("[LGPE] valores com nome e código", ggWork.Number.Length == 5 && ggWork.Number[1] == '#', $"{ggWork.Number} {ggWorkName}");
gg.Tab = 3;
var titles = gg.ShortcutRows.First();
Check("[LGPE] atalho de títulos disponível", titles.IsReady, titles.Name);
titles.ApplyCommand.Execute(null);
Check("[LGPE] títulos liberados", Wait(() => !titles.IsReady), titles.State);
var ggSaved = new SAV7b(gg0.Write().ToArray());
var ggEd = new GameEditors(ggSaved);
Check("[LGPE] flag do Articuno gravada", ggEd.GetFlag(ggEd.Flags.First(f => f.Name.Contains("Articuno")).Index));
Check("[LGPE] valor gravado", ggEd.GetWork(ggEd.Works.First(w => w.Name == ggWorkName).Index) == 3, ggWorkName);
Check("[LGPE] títulos gravados", Enumerable.Range(0, EventWork7b.MaxTitleFlag).All(i => ggSaved.Blocks.EventWork.GetTitleFlag(i)));

// ---------- Estojos e Pokeathlon (copias do Sapphire, HeartGold e Omega Ruby; Platinum e BD/SP sinteticos) ----------
// Platinum em branco nao grava (o Core nao calcula checksum de save vazio do Gen 4): confere no save em memoria
var pt0 = (SAV4Sinnoh)BlankSaveFile.Get(GameVersion.Pt);
var ptEd = new GameEditors(pt0);
var poffins = ptEd.Shortcuts.First(x => x.Name == "Estojo de Poffins cheio");
Check("[Pt] atalho do estojo de Poffins disponível", poffins.Ready());
poffins.Apply();
Check("[Pt] estojo de Poffins cheio", !poffins.Ready() && new PoffinCase4(pt0).Poffins.All(x => x.Smoothness == 255));
foreach (var (file, names) in new[]
{
    ("sa.sav", new[] { "Estojo de Pokéblocks cheio" }),
    ("hg.sav", new[] { "Pontos do Pokéathlon no máximo", "Todos os Data Cards do Pokéathlon", "Todas as medalhas do Pokéathlon" }),
    ("or.sav", new[] { "999 Pokéblocks de cada" }),
    ("BD.sav", new[] { "Estojo de Poffins cheio" }),
})
{
    var path = Path.Combine(work, file);
    if (!File.Exists(path)) { Console.WriteLine($"(sem {file}: pulado)"); continue; }
    vm.Open(path); Pump();
    var gc = vm.Game;
    vm.CurrentPage = gc; gc.Tab = 3; Pump();
    foreach (var name in names)
    {
        var row = gc.ShortcutRows.FirstOrDefault(r => r.Name == name);
        Check($"[{file}] atalho \"{name}\" disponível", row is { IsReady: true }, string.Join(", ", gc.ShortcutRows.Select(r => r.Name)));
        if (row is null) continue;
        row.ApplyCommand.Execute(null);
        Check($"[{file}] \"{name}\" aplicado", Wait(() => !row.IsReady), row.State);
    }
    if (file == "hg.sav") Shot("hgss-shortcuts");
    var reopened = Reopen(file);
    var ec = new GameEditors(reopened);
    Check($"[{file}] estojo/Pokéathlon gravado", names.All(n => ec.Shortcuts.First(s => s.Name == n).Ready() == false));
}

// ---------- Z-A (copia do save real) ----------
var zaPath = Path.Combine(work, "za.sav");
if (File.Exists(zaPath))
{
    vm.Open(zaPath); Pump();
    var gz = vm.Game;
    vm.CurrentPage = gz; Pump();
    Check("[ZA] página Jogo com flags, valores e atalhos", vm.Pages.Contains(gz) && gz.HasEvents && gz.HasWorks && gz.HasShortcuts, gz.Summary);
    Check("[ZA] categorias das tabelas", gz.Categories.Contains("Itens do mapa") && gz.Categories.Contains("Missões"), string.Join(", ", gz.Categories));
    Check("[ZA] itens do mapa com nome (Colorful Screw, TM)", gz.FlagRows.Any(f => f.Name.StartsWith("Colorful Screw")) && gz.FlagRows.Any(f => f.Name.StartsWith("TM")), gz.CountText);
    gz.ShowUnnamed = true; Pump();
    var zf = gz.FlagRows.First(f => f.Category == "Eventos");
    var zfCode = zf.Number; var zfBefore = zf.IsSet;
    gz.Query = zfCode[..10]; Pump();
    Check("[ZA] busca pelo hash", gz.FlagRows.Any(f => f.Number == zfCode), gz.CountText);
    zf.IsSet = !zfBefore; Pump();
    gz.Query = "";
    Shot("za-flags");
    gz.Tab = 1; Pump();
    var zw = gz.WorkRows.First(w => w.Category == "Missões");
    var zwCode = zw.Number; var zwNew = zw.Value == 5 ? 6 : 5;
    zw.ValueNumber = zwNew; Pump();
    gz.Tab = 3; Pump();
    var screws = gz.ShortcutRows.First(r => r.Name.Contains("Colorful Screws"));
    var za = (SAV9ZA)vm.ActiveTab!.Sav;
    // Devolve 3 screws ao mapa (e baixa a mochila) para o atalho ter o que fazer, como num save no meio do jogo
    foreach (var (item, _) in ColorfulScrew9a.GetScrewLocations(za, true).Take(3).ToList())
        za.Blocks.FieldItems.SetValue(za.Blocks.FieldItems.GetIndex(FnvHash.HashFnv1a_64(item)), false);
    za.Items.SetItemQuantity(ColorfulScrew9a.ColorfulScrewItemIndex, 40);
    screws.Refresh(); Pump();
    var missing = ColorfulScrew9a.GetScrewLocations(za, false).Count();
    var screwQty = za.Items.GetItemQuantity(ColorfulScrew9a.ColorfulScrewItemIndex);
    Check("[ZA] atalho dos Colorful Screws disponível", screws.IsReady && missing == 3, $"faltam {missing}");
    screws.ApplyCommand.Execute(null);
    Check("[ZA] Colorful Screws pegos", Wait(() => !screws.IsReady), $"faltavam {missing}");
    Shot("za-shortcuts");
    var zaSaved = (SAV9ZA)Reopen("za.sav");
    var zEd = new GameEditors(zaSaved);
    Check("[ZA] flag gravada", zEd.GetFlag(zEd.Flags.First(f => f.Code == zfCode).Index) == !zfBefore, zfCode);
    Check("[ZA] valor gravado", zEd.GetWork(zEd.Works.First(w => w.Code == zwCode).Index) == zwNew, zwCode);
    Check("[ZA] screws gravados e na mochila", !ColorfulScrew9a.GetScrewLocations(zaSaved, false).Any()
        && zaSaved.Items.GetItemQuantity(ColorfulScrew9a.ColorfulScrewItemIndex) == Math.Min(100, screwQty + missing),
        $"{screwQty} + {missing} -> {zaSaved.Items.GetItemQuantity(ColorfulScrew9a.ColorfulScrewItemIndex)}");
}
else
    Console.WriteLine("(sem za.sav: parte do Z-A pulada)");
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
