using System;
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

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
var sav = CoreAdapter.LoadSave(Path.Combine(work, "moon.sav"))!; CoreAdapter.Activate(sav);
var pk = (PK7)sav.BoxData.Concat(sav.PartyData).First(p => p.Species > 0); var original = pk.Data.ToArray();
var message = QrImages.Message(pk, 2, 4, 1);
var decoded = QRMessageUtil.GetPKM(message, EntityContext.Gen7);
Check("Moon mensagem preserva espécie PID e IVs", decoded is PK7 && decoded.Species == pk.Species && decoded.PID == pk.PID && decoded.IVs.SequenceEqual(pk.IVs));
Check("QR inclui posição e cópias", message.Length == QR7.SIZE && message[8] == 2 && message[12] == 4 && message[16] == 1);
Check("gerar não altera Pokémon", pk.Data.SequenceEqual(original));
var png = QrImages.Png(message); File.WriteAllBytes(Path.Combine(work, "moon-qr-private.png"), png);
Check("gera PNG sem System.Drawing", png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
var editor = new PokemonEditorViewModel(pk, "Teste", _ => { }, Console.WriteLine, sav: sav);
Check("Gen7 botão QR", editor.HasQr);
Check("Gen6 esconde botão Pokémon", !new PokemonEditorViewModel(new PK6(), "Teste", _ => { }, _ => { }, sav: BlankSaveFile.Get(GameVersion.X)).HasQr);
var card = new WC6 { CardID = 1, Species = 25, CardTitle = "Demo" };
var cardMessage = QRMessageUtil.GetMessage(card);
Check("QR cartão preserva arquivo", Convert.FromBase64String(cardMessage[(cardMessage.IndexOf('#') + 1)..]).SequenceEqual(card.Write().ToArray()));

// Captura publica usa apenas um PK7 sintetico.
var demo = BlankSaveFile.Get(GameVersion.MN); CoreAdapter.Activate(demo);
var demoPk = EncounterDatabase.SearchEncounters(demo, 25, true).Where(e => e.Species == 25).Select(e => EncounterDatabase.ToEntity(demo, e, out _)).OfType<PK7>().First();
var main = new MainViewModel(); byte[]? saved = null; main.SaveQrImage = b => { saved = b; return Task.CompletedTask; };
main.ShowPokemonQr(demoPk); Check("sobreposição aberta", main.HasQr && main.Qr!.Image.PixelSize.Width > 0);
main.Qr!.SaveCommand.Execute(null); Check("salvar imagem usa bytes do PNG", saved is not null && saved.SequenceEqual(main.Qr.Png) && !main.IsDirty);
var win = new MainWindow { DataContext = main, Width = 1180, Height = 720 }; win.Show();
for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
win.CaptureRenderedFrame()!.Save(Path.Combine(work, "qr-overlay.png")); main.CloseQr(); Check("fecha sobreposição", !main.HasQr); win.Close();
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
