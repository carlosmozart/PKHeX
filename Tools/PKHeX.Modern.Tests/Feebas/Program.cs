using System;
using System.Linq;
using System.IO;
using PKHeX.Core;
using Avalonia;
using Avalonia.Headless;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;
using Avalonia.Threading;
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
PKM Make(SaveFile sav)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, 349, true).Where(e => e.Species == 349))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk && new LegalityAnalysis(pk).Valid) return pk;
    throw new Exception("Não gerou Feebas legal em " + sav.Version);
}
foreach (var version in new[] { GameVersion.E, GameVersion.FR, GameVersion.Pt, GameVersion.HG, GameVersion.B, GameVersion.X, GameVersion.AS, GameVersion.US, GameVersion.SH, GameVersion.BD, GameVersion.SL, GameVersion.ZA })
{
    var sav = BlankSaveFile.Get(version);
    CoreAdapter.Activate(sav);
    Console.WriteLine(version + ": " + string.Join(" | ", EvolutionTree.GetEvolutionTree(sav.Context).Forward.GetForward(349, 0).ToArray().Select(m => m.Method + " argument=" + m.Argument)));
    // FR/HG recebem Feebas por troca; para Beauty nos jogos sem concursos, usa origem Emerald.
    var source = version is GameVersion.FR or GameVersion.HG or GameVersion.B or GameVersion.X or GameVersion.US or GameVersion.SH or GameVersion.SL
        ? BlankSaveFile.Get(GameVersion.E) : sav;
    var pk = Make(source);
    var stats = (IContestStats)pk;
    stats.ContestBeauty = version == GameVersion.ZA ? (byte)0 : (byte)170;
    var (_, min, max) = CoreAdapter.GetContestRule(pk);
    stats.ContestSheen = min <= max ? (byte)min : (byte)0;
    if (source != sav) pk = CoreAdapter.ConvertForSave(sav, pk, out var error) ?? throw new Exception(error);
    if (source != sav && pk is IHomeTrack home) home.Tracker = 0x123456789ABCDEF;
    Check(version + " felicidade não oferece evolução", CoreAdapter.GetFriendshipEvolutions(pk, sav).Count == 0);
    var options = CoreAdapter.GetBeautyEvolutions(pk, sav);
    Check(version + " método Beauty por jogo", options.Count == (version == GameVersion.ZA ? 0 : 1));
    if (options.Count > 0)
    {
        var low = pk.Clone(); ((IContestStats)low).ContestBeauty = 169; low.CurrentFriendship = 255;
        Check(version + " felicidade máxima não substitui Beauty", CoreAdapter.GetBeautyEvolutions(low, sav).Single().Blocked is not null);
        var editor = new PokemonEditorViewModel(pk, "Teste", _ => { }, Console.WriteLine, sav: sav, legalMode: true);
        editor.BeautyEvolutions.Single().Command.Execute(null);
        Check(version + " Beauty evolui legal", editor.SpeciesName == "Milotic" && editor.CanApply);
        var grown = pk.Clone(); int level = grown.CurrentLevel; uint pid = grown.PID; var friendship = grown.CurrentFriendship;
        CoreAdapter.EvolveByBeauty(grown, options.Single(), sav);
        Check(version + " preserva PID, Beauty e felicidade", grown.PID == pid && ((IContestStats)grown).ContestBeauty == 170 && grown.CurrentFriendship == friendship && grown.CurrentLevel == level + 1);
        low.IsEgg = true; Check(version + " ovo bloqueado", CoreAdapter.GetBeautyEvolutions(low, sav).Count == 0);
        low.IsEgg = false; low.HeldItem = Array.IndexOf(CoreAdapter.GetItemNames(low).ToArray(), "Everstone");
        Check(version + " Everstone bloqueia Beauty", CoreAdapter.GetBeautyEvolutions(low, sav).Single().Blocked is not null);
    }
    var trades = CoreAdapter.GetTradeEvolutions(pk);
    bool supportsTrade = pk.Format >= 5 && version != GameVersion.BD;
    Check(version + " troca Prism Scale por jogo", trades.Count == (supportsTrade ? 1 : 0));
    if (trades.Count > 0)
    {
        Check(version + " permite troca sem Prism Scale", trades.Single().Blocked is null);
        var withoutItem = pk.Clone();
        CoreAdapter.EvolveByTrade(withoutItem, trades.Single(), sav);
        var noItemAnalysis = new LegalityAnalysis(withoutItem);
        if (!noItemAnalysis.Valid) Console.WriteLine(noItemAnalysis.Report());
        Check(version + " troca sem item legal", withoutItem.Species == 350 && noItemAnalysis.Valid);
        pk.HeldItem = trades.Single().ItemId;
        var evo = CoreAdapter.GetTradeEvolutions(pk).Single();
        CoreAdapter.EvolveByTrade(pk, evo, sav);
        Check(version + " troca legal e escala consumida", pk.Species == 350 && pk.HeldItem == 0 && new LegalityAnalysis(pk).Valid);
    }
}
var visualSave = BlankSaveFile.Get(GameVersion.BD); CoreAdapter.Activate(visualSave);
var visual = Make(visualSave); ((IContestStats)visual).ContestBeauty = 169;
((IContestStats)visual).ContestSheen = CoreAdapter.GetContestRule(visual).MinSheen;
visualSave.SetBoxSlotAtIndex(visual, 0, 0);
var dir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-feebas-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
var path = Path.Combine(dir, "feebas.sav"); File.WriteAllBytes(path, visualSave.Write().ToArray());
var main = new MainViewModel(); main.Open(path); main.SelectSlotAsync(main.Boxes.Slots[0]).GetAwaiter().GetResult();
var win = new MainWindow { DataContext = main, Width = 1500, Height = 1100 }; win.Show();
for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
win.CaptureRenderedFrame()!.Save(Path.Combine(dir, "feebas.png")); Console.WriteLine("Captura: " + dir);
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
