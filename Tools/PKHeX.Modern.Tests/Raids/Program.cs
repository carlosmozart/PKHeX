using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
foreach (var file in new[] { "sh.sav", "sv.sav" })
{
    var path = Path.Combine(work, file); var sav = CoreAdapter.LoadSave(path)!; CoreAdapter.Activate(sav);
    int confirmations = 0, changes = 0; bool confirm = false;
    var page = new GamePageViewModel((_, _, _) => { confirmations++; return Task.FromResult(confirm); }, Console.WriteLine);
    page.Changed = () => changes++; page.Load(sav); page.Tab = 6;
    Check(file + " raids por região", page.HasRaids && page.IsRaidsTab && page.RaidRegions.Count >= (sav is SAV8SWSH ? 3 : 2));
    foreach (var region in page.RaidRegions.Where(r => r.CanActivate))
    {
        page.RaidRegion = region;
        var row = region.Rows.First(r => r.IsSwsh ? r.Index != 16 : r.HasPosition);
        var originalSeed = row.Seed; var positions = region.Rows.Select(r => r.Position).ToArray();
        bool wasActive = row.Active; int before = changes;
        page.SetAllRaidsAsync(true).GetAwaiter().GetResult();
        Check(region.Name + " cancelar preserva save", row.Active == wasActive && changes == before);
        confirm = true; page.SetAllRaidsAsync(true).GetAwaiter().GetResult();
        Check(region.Name + " ativar todas", region.Rows.Where(r => r.IsSwsh ? r.Index != 16 : r.HasPosition).All(r => r.Active));
        Check(region.Name + " preserva seed e posição", row.Seed == originalSeed && region.Rows.Select(r => r.Position).SequenceEqual(positions));
        row.Seed = row.IsSwsh ? "0123456789ABCDEF" : "89ABCDEF"; row.TypeIndex = row.IsSwsh ? 2 : 1;
        if (row.IsSwsh) { row.Stars = 5; row.Roll = 73; row.Watts = true; row.Hash = "123456789ABCDEF0"; }
        else { row.LeaguePoints = true; region.CurrentSeed = "123456789ABCDEF0"; region.TomorrowSeed = "FEDCBA9876543210"; }
        string expectedSeed = row.Seed; row.Seed = row.IsSwsh ? "10000000000000000" : "100000000";
        Check(region.Name + " rejeita seed fora do limite", row.Seed == expectedSeed);
        var output = Path.Combine(work, file + "-" + page.RaidRegions.ToList().IndexOf(region)); File.WriteAllBytes(output, sav.Write().ToArray());
        var read = CoreAdapter.LoadSave(output)!; var reopened = new GamePageViewModel((_, _, _) => Task.FromResult(true), _ => { }); reopened.Load(read);
        var rr = reopened.RaidRegions.First(r => r.Name == region.Name).Rows[row.Index];
        Check(region.Name + " reabre seed tipo e checksum", read.ChecksumsValid && rr.Seed == expectedSeed && rr.TypeIndex == row.TypeIndex && rr.Active);
        Check(region.Name + " reabre flags e estrelas", row.IsSwsh ? rr.Stars == 5 && rr.Roll == 73 && rr.Watts && rr.Hash == row.Hash : rr.LeaguePoints);
        row.Active = false; File.WriteAllBytes(output, sav.Write().ToArray()); read = CoreAdapter.LoadSave(output)!; reopened.Load(read);
        Check(region.Name + " desativar uma persiste", read.ChecksumsValid && !reopened.RaidRegions.First(r => r.Name == region.Name).Rows[row.Index].Active);
        page.SetAllRaidsAsync(false).GetAwaiter().GetResult();
        Check(region.Name + " desativar todas", region.Rows.Where(r => r.IsSwsh ? r.Index != 16 : r.HasPosition).All(r => !r.Active));
        confirm = false;
    }
    Check(file + " operações em lote confirmadas", confirmations > 0 && changes > 0);
    if (page.RaidRegions.FirstOrDefault(r => !r.CanActivate) is { } seven)
    {
        page.RaidRegion = seven; var row = seven.Rows[0]; row.Identifier = 20261004; row.Captured = true; row.Defeated = true;
        Check("7 estrelas não oferece ativação em lote", !page.ActivateRaidsCommand.CanExecute(null));
        File.WriteAllBytes(path, sav.Write().ToArray()); var read = (SAV9SV)CoreAdapter.LoadSave(path)!; var r = read.RaidSevenStar.GetRaid(0);
        Check("7 estrelas reabre registro com checksum", read.ChecksumsValid && r.Identifier == 20261004 && r.Captured && r.Defeated);
    }
}
var unsupported = new GamePageViewModel((_, _, _) => Task.FromResult(true), _ => { }); unsupported.Load(BlankSaveFile.Get(GameVersion.MN));
Check("outros jogos escondem raids", !unsupported.HasRaids);
// Todas as capturas públicas usam saves sintéticos.
foreach (var version in new[] { GameVersion.SH, GameVersion.SL })
{
    var demo = BlankSaveFile.Get(version); demo.OT = "Demo"; CoreAdapter.Activate(demo);
    if (demo is SAV8SWSH sh) sh.RaidGalar.GetRaid(0).Activate(4, 73, true);
    if (demo is SAV9SV sv) { var r = sv.RaidPaldea.GetRaid(0); r.AreaID = 1; r.IsEnabled = true; r.Content = TeraRaidContentType.Black6; r.Seed = 0x89ABCDEF; }
    var page = new GamePageViewModel((_, _, _) => Task.FromResult(true), _ => { }); page.Load(demo); page.Tab = 6;
    Check(version + " regiões sintéticas", page.RaidRegions.Count == (version == GameVersion.SH ? 3 : 4));
    if (demo is SAV9SV dlc)
    {
        foreach (var region in page.RaidRegions.Where(r => r.CanActivate && r.Name != "Paldea"))
        {
            var list = region.Name == "Kitakami" ? dlc.RaidKitakami : dlc.RaidBlueberry;
            list.GetRaid(0).AreaID = 1; region.SetAll(true); region.Rows[0].Seed = "12345678"; region.Rows[0].LeaguePoints = true;
            Check(region.Name + " DLC seed e flags", list.GetRaid(0).IsEnabled && list.GetRaid(0).Seed == 0x12345678 && list.GetRaid(0).IsClaimedLeaguePoints);
            Check(region.Name + " ignora posições vazias", !list.GetRaid(1).IsEnabled);
        }
    }
    var win = new Window { Content = new ContentControl { Content = page, Margin = new Thickness(16) }, Width = 948, Height = 620 }; win.Show();
    for (int i = 0; i < 15; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    win.CaptureRenderedFrame()!.Save(Path.Combine(work, "raids-" + version + ".png")); win.Close();
}
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
