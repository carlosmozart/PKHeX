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

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; int fails = 0;
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
void Check(string n, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {n}"); if (!ok) fails++; }
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
bool? answer = true;
ConfirmDialogViewModel? lastDialog = null;
MainViewModel? vmRef = null;
bool Run(Func<Task> action)
{
    var task = action(); var sw = Stopwatch.StartNew();
    while (!task.IsCompleted && sw.ElapsedMilliseconds < 60000)
    {
        Pump();
        if (vmRef!.Dialog is { } d) { lastDialog = d; if (answer is { } a) d.Complete(a); }
        System.Threading.Thread.Sleep(10);
    }
    Pump(); return task.IsCompleted;
}

const string team = """
=== [gen5] Teste ===

Lillipup @ Oran Berry
Ability: Vital Spirit
Level: 12
- Tackle
- Bite

Pikachu (M) @ Light Ball
Ability: Static
Level: 50
EVs: 252 SpA / 4 SpD / 252 Spe
Timid Nature
- Thunderbolt
- Volt Switch

Sprigatito
Ability: Overgrow
- Scratch
""";
var sets = ShowdownBatch.Split(team);
Check("separa os sets e ignora o cabeçalho do Teambuilder", sets.Count == 3 && sets[0].StartsWith("Lillipup") && sets[2].StartsWith("Sprigatito"));

var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "DEMO";
var path = Path.Combine(work, "Black.sav"); File.WriteAllBytes(path, sav.Write().ToArray());
var vm = new MainViewModel(new AppSettings { CheckForUpdates = false }); vmRef = vm;
vm.Open(path); vm.LegalMode = false; vm.CurrentPage = vm.Party; Pump();
string clipboard = team; string? copied = null;
vm.ReadClipboard = () => Task.FromResult<string?>(clipboard);
vm.WriteClipboard = text => { copied = text; return Task.CompletedTask; };

// Equipe, modo legal desligado: o set da Gen 9 fica de fora com o motivo; os outros entram num unico desfazer.
answer = true;
Check("colar na equipe terminou", Run(() => vm.PasteShowdownTeamAsync(toParty: true)));
var party = vm.ActiveTab!.Sav;
Check("dois entram na equipe", party.PartyCount == 2 && party.GetPartySlotAtIndex(0).Species == (ushort)Species.Lillipup && party.GetPartySlotAtIndex(1).Species == (ushort)Species.Pikachu);
var pika = party.GetPartySlotAtIndex(1);
Check("set aplicado (nível, item, golpe, natureza)", pika.CurrentLevel == 50 && pika.Move1 == (ushort)Move.Thunderbolt && pika.Nature == Nature.Timid && pika.HeldItem > 0);
Check("prévia lista o que fica de fora", lastDialog?.Details.Any(d => d.StartsWith("✗ Sprigatito")) == true && lastDialog.Details.Count == 3);
Check("alteração marcada", vm.IsDirty);
vm.UndoCommand.Execute(null); Pump();
Check("um Ctrl+Z desfaz o lote", vm.ActiveTab!.Sav.PartyCount == 0);

// O "Colar Showdown" do editor também recusa uma espécie que o formato não comporta (não vira outra).
var pk5 = new PK5 { Species = (ushort)Species.Patrat };
Check("editor recusa espécie de geração futura", CoreAdapter.ApplyShowdown(pk5, "Sprigatito\n- Scratch") is not null && pk5.Species == (ushort)Species.Patrat);

// Cancelar não muda nada.
answer = false; lastDialog = null;
Run(() => vm.PasteShowdownTeamAsync(toParty: true));
Check("cancelar não cola", vm.ActiveTab!.Sav.PartyCount == 0 && lastDialog is not null);

// Caixa: slots vazios da caixa atual; um slot ocupado é pulado.
answer = true; vm.CurrentPage = vm.Boxes; vm.Boxes.CurrentBox = 2; Pump();
var occupied = new PK5 { Species = (ushort)Species.Patrat, CurrentLevel = 5, OriginalTrainerName = "DEMO", Version = GameVersion.B };
vm.ActiveTab!.Sav.SetBoxSlotAtIndex(occupied, 2, 0); Pump();
Run(() => vm.PasteShowdownTeamAsync(toParty: false));
var s2 = vm.ActiveTab!.Sav;
Check("caixa recebe nos vazios sem sobrescrever", s2.GetBoxSlotAtIndex(2, 0).Species == (ushort)Species.Patrat
    && s2.GetBoxSlotAtIndex(2, 1).Species == (ushort)Species.Lillipup && s2.GetBoxSlotAtIndex(2, 2).Species == (ushort)Species.Pikachu);

// Copiar a caixa inteira no formato Showdown.
Run(() => { vm.Boxes.CopyShowdownCommand!.Execute(null); return Task.Delay(50); });
Check("copiar caixa gera três sets", copied is not null && ShowdownBatch.Split(copied).Count == 3 && copied.Contains("Thunderbolt") && copied.Contains("Patrat"));

// Sem espaço: avisa e não pergunta.
for (int i = 0; i < 6; i++) vm.ActiveTab!.Sav.SetPartySlotAtIndex(occupied.Clone(), i);
lastDialog = null; Run(() => vm.PasteShowdownTeamAsync(toParty: true));
Check("sem vagas: avisa sem perguntar", lastDialog is null && vm.Status.Contains("0 vaga"));
for (int i = 5; i >= 0; i--) vm.ActiveTab!.Sav.SetPartySlotAtIndex(vm.ActiveTab.Sav.BlankPKM, i);

// Modo legal: o que entra é legal (gerado de novo a partir de um encontro quando preciso).
vm.LegalMode = true; clipboard = "Lillipup @ Oran Berry\nAbility: Vital Spirit\nLevel: 12\n- Tackle\n- Bite";
vm.CurrentPage = vm.Party; Run(() => vm.PasteShowdownTeamAsync(toParty: true));
var legal = vm.ActiveTab!.Sav.PartyCount == 1 ? vm.ActiveTab.Sav.GetPartySlotAtIndex(0) : null;
Check("modo legal: entra legal", legal is not null && legal.Species == (ushort)Species.Lillipup && new LegalityAnalysis(legal).Valid);
Check("arquivo não foi gravado", File.ReadAllBytes(path).SequenceEqual(sav.Write().ToArray()));

// Capturas: desktop e celular (o botão 📋 Showdown cabe no cabeçalho da caixa e da equipe).
vm.CurrentPage = vm.Boxes; var desk = new PKHeX.Modern.Views.MainWindow { DataContext = vm, Width = 1440, Height = 950 }; desk.Show();
for (int i = 0; i < 6; i++) Pump(); desk.CaptureRenderedFrame()?.Save(Path.Combine(work, "showdown-box-desktop.png")); desk.Hide(); // fechar perguntaria sobre as alterações
App.ShowShortcuts = false;
var phone = new Avalonia.Controls.Window { Width = 892, Height = 412, Content = new PKHeX.Modern.Views.MobileShell(vm) }; phone.Show();
for (int i = 0; i < 6; i++) Pump(); phone.CaptureRenderedFrame()?.Save(Path.Combine(work, "showdown-box-mobile.png"));
vm.CurrentPage = vm.Party; for (int i = 0; i < 6; i++) Pump(); phone.CaptureRenderedFrame()?.Save(Path.Combine(work, "showdown-party-mobile.png")); phone.Close();

Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;

