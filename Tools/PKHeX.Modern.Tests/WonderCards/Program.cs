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
using Cards = PKHeX.Modern.Services.WonderCards;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
DataMysteryGift Gift(SaveFile sav) => EncounterDatabase.LoadGifts(sav, true).OfType<DataMysteryGift>().First(g => !g.IsEmpty && g.IsCardCompatible(sav, out _) && g is not PGT && g is not PCD { IsLockCapsule: true });
foreach (var file in new[] { "hg.sav", "bw.sav", "or.sav" })
{
    var path = Path.Combine(work, file); var sav = CoreAdapter.LoadSave(path)!; CoreAdapter.Activate(sav);
    while (Array.FindIndex(Cards.Read(sav), c => !c.IsEmpty) is >= 0 and var pos) Cards.Delete(sav, pos);
    var gift = Gift(sav); int slot = Cards.Add(sav, gift, out var error);
    Check(file + " adiciona evento do banco: " + error, slot >= 0);
    if (slot < 0) continue;
    var giftPath = Path.Combine(work, "gift." + gift.Extension); File.WriteAllBytes(giftPath, gift.Write().ToArray());
    var fromFile = (DataMysteryGift)MysteryGift.GetMysteryGift(File.ReadAllBytes(giftPath), Path.GetExtension(giftPath))!;
    int second = Cards.Add(sav, fromFile, out error); Check(file + " importa cartão de arquivo: " + error, second >= 0);
    File.WriteAllBytes(path, sav.Write().ToArray()); var read = CoreAdapter.LoadSave(path)!; var album = Cards.Read(read);
    Check(file + " cartão persiste com checksum", read.ChecksumsValid && album[slot].GetType() == gift.GetType() && album[slot].CardID == gift.CardID);
    if (sav is SAV4HGSS hg)
    {
        Check("Gen4 entrega ativa e PGT ligado", ((IMysteryGiftStorageProvider)hg).MysteryGiftStorage is MysteryBlock4 { IsDeliveryManActive: true } && Cards.Read(sav).OfType<PGT>().Any(g => g.Slot == slot - 8));
        var manaphy = new PGT { GiftType = GiftType4.ManaphyEgg };
        int egg = Cards.Add(sav, manaphy, out error); Check("Manaphy usa slot PGT", egg >= 0 && Cards.Read(sav)[egg] is PGT { IsManaphyEgg: true });
        var capsule = new PCD(); capsule.IsItem = true; capsule.ItemID = 533; capsule.CardID = 999;
        int special = Cards.Add(sav, capsule, out error); Check("Lock Capsule usa slot especial", special == 11 && !Cards.Read(sav)[11].IsEmpty);
    }
    Cards.MarkUnused(sav, slot); File.WriteAllBytes(path, sav.Write().ToArray()); read = CoreAdapter.LoadSave(path)!;
    Check(file + " flag e cartão não recebidos", read.ChecksumsValid && !Cards.Read(read)[slot].GiftUsed);
    var storage = ((IMysteryGiftStorageProvider)read).MysteryGiftStorage;
    if (storage is IMysteryGiftFlags flags && gift.CardID > 0 && gift.CardID < flags.MysteryGiftReceivedFlagMax)
        Check(file + " limpa flag", !flags.GetMysteryGiftReceivedFlag(gift.CardID));
    if (storage is MysteryBlock5 five) five.EndAccess();
    int before = Cards.Read(sav).Count(c => !c.IsEmpty); Cards.Delete(sav, slot); File.WriteAllBytes(path, sav.Write().ToArray()); read = CoreAdapter.LoadSave(path)!;
    Check(file + " apaga e reabre", read.ChecksumsValid && Cards.Read(read).Count(c => !c.IsEmpty) < before);
    var wrong = gift.Generation == 5 ? (DataMysteryGift)new WC6 { CardID = 1, Species = 25 } : new PGF { CardID = 1, Species = 25 };
    Check(file + " recusa outra geração", Cards.Add(sav, wrong, out error) < 0 && error.Length > 0);
}
Check("Gen3 sem cartões", !Cards.Supported(BlankSaveFile.Get(GameVersion.E)));
var demo = BlankSaveFile.Get(GameVersion.B); CoreAdapter.Activate(demo);
while (Array.FindIndex(Cards.Read(demo), c => !c.IsEmpty) is >= 0 and var demoSlot) Cards.Delete(demo, demoSlot);
var demoPath = Path.Combine(work, "demo.sav"); File.WriteAllBytes(demoPath, demo.Write().ToArray());
var main = new MainViewModel(); main.Open(demoPath); main.CurrentPage = main.Game;
Check("view model adiciona e marca alteração", main.Game.AddCard(Gift(demo)) && main.IsDirty && main.Game.IsCardsTab);
Loc.Load(Loc.English); Loc.Hook();
Check("posição vazia traduzida", Loc.T(main.Game.CardRows.First(c => c.Gift.IsEmpty).Title).EndsWith("Empty"));
Check("detalhes pendentes traduzidos", !Loc.T(main.Game.CardRows.First(c => !c.Gift.IsEmpty).Details).Contains("Pendente"));
var win = new MainWindow { DataContext = main, Width = 1440, Height = 950 }; win.Show();
for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
win.CaptureRenderedFrame()!.Save(Path.Combine(work, "wonder-cards.png")); win.Close();
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
