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

// Edição em lote: escopos, ações rápidas, script do PKHeX, pré-visualização sem gravar, aplicar, desfazer e modo legal.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-batch-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
PKM Make(SaveFile sav, ushort species)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, species, true).Where(e => e.Species == species))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk && new LegalityAnalysis(pk).Valid) return pk;
    throw new Exception("Sem encontro legal: " + species);
}
var black = BlankSaveFile.Get(GameVersion.B); black.OT = "Demo"; CoreAdapter.Activate(black);
int n = 0;
foreach (ushort sp in new ushort[] { 495, 498, 501, 25 }) black.SetBoxSlotAtIndex(Make(black, sp), 0, n++);
black.SetBoxSlotAtIndex(Make(black, 570), 1, 0);
// Ilegal para o "Legalizar": Lillipup com nível de encontro impossível
var broken = Make(black, 506); broken.MetLevel = 99; broken.RefreshChecksum(); black.SetBoxSlotAtIndex(broken, 2, 0);
black.SetPartySlotAtIndex(Make(black, 504), 0);
var path = Path.Combine(work, "Black.sav"); File.WriteAllBytes(path, black.Write().ToArray());

var settings = new AppSettings { CheckForUpdates = false, LegalMode = true };
var vm = new MainViewModel(settings);
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int k = 10) { for (int i = 0; i < k; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
bool answer = true;
bool Wait(Func<bool> done, int ms = 20000) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms && !done()) { Pump(2); if (vm.Dialog is { } d) d.Complete(answer); System.Threading.Thread.Sleep(10); } return done(); }
void Shot(string name) { Pump(15); win.UpdateLayout(); Pump(5); win.CaptureRenderedFrame()?.Save(Path.Combine(work, name + ".png")); }
SaveFile Sav() => vm.ActiveTab!.Sav;
int Level(int box, int slot) => CoreAdapter.GetBoxSlot(Sav(), box, slot).CurrentLevel;

vm.Open(path); Pump();
var b = vm.Batch;
Check("Edição em lote está nas páginas", vm.Pages.Contains(b));
vm.CurrentPage = b; Pump();
Check("sem painel do editor na página", !vm.ShowEditorPanel);

// Sem ação: erro amigável
b.PreviewCommand.Execute(null); Wait(() => !b.IsBusy);
Check("sem ação pede uma ação", b.HasError, b.Error);

// Pré-visualizar Nível 100 na caixa atual: 4 alterações, nada gravado
int before = Level(0, 0);
b.ScopeIndex = (int)BatchScope.CurrentBox;
b.Actions.First(a => a.Name == "Nível 100").IsChecked = true;
b.PreviewCommand.Execute(null);
Check("pré-visualização da caixa 1 = 3 (o Pikachu de evento já é Nv. 100)", Wait(() => !b.IsBusy && b.Preview.Count == 3) && b.Preview.All(p => p.Name != "Pikachu"), b.Summary);
Check("pré-visualização não grava", Level(0, 0) == before && !vm.IsDirty);
Check("diferença mostra só o nível", b.Preview[0].Diff.Contains("→ 100") && !b.Preview[0].Diff.Contains("curado"), b.Preview[0].Diff);
Shot("batch-preview");

// Aplicar: grava, marca alteração e desfaz com Ctrl+Z
b.ApplyCommand.Execute(null);
Check("aplicou nível 100 nos 3", Wait(() => Level(0, 0) == 100 && Level(0, 2) == 100), b.Summary);
Check("caixa 2 e equipe ficaram de fora", Level(1, 0) < 100 && CoreAdapter.GetPartySlot(Sav(), 0).CurrentLevel < 100);
Check("marca alteração não salva", vm.IsDirty);
vm.UndoCommand.Execute(null); Pump();
Check("Ctrl+Z desfaz o lote inteiro", Level(0, 0) == before && Level(0, 2) < 100);

// Script: filtro por espécie em caixas + equipe
b.Actions.First(a => a.Name == "Nível 100").IsChecked = false;
b.ScopeIndex = (int)BatchScope.BoxesAndParty;
b.Script = "=Species=Pikachu\n.CurrentLevel=50";
b.PreviewCommand.Execute(null); Wait(() => !b.IsBusy);
Check("script filtra só o Pikachu", b.Preview.Count == 1 && b.Preview[0].Name == "Pikachu", b.Summary);
b.Script = "=Species=Patrat\n.CurrentLevel=60";
b.ScopeIndex = (int)BatchScope.Party;
b.ApplyCommand.Execute(null);
Check("equipe: Patrat no nível 60", Wait(() => CoreAdapter.GetPartySlot(Sav(), 0).CurrentLevel == 60), b.Summary);

// Script inválido
b.Script = "isso não é script";
b.PreviewCommand.Execute(null); Wait(() => !b.IsBusy);
Check("script inválido mostra erro", b.HasError && b.Preview.Count == 0, b.Error);
b.Script = ".PropriedadeQueNaoExiste=1";
b.PreviewCommand.Execute(null); Wait(() => !b.IsBusy);
Check("propriedade desconhecida mostra erro", b.HasError && b.Error.Contains("PropriedadeQueNaoExiste"), b.Error);

// Modo legal: os iniciais (presente) com Master Ball ficam ilegais e são pulados
b.Script = ".Ball=Master Ball";
b.ScopeIndex = (int)BatchScope.CurrentBox;
b.PreviewCommand.Execute(null); Wait(() => !b.IsBusy);
int illegal = b.Preview.Count(p => p.BecomesIllegal);
Check("pré-visualização marca os que ficariam ilegais", illegal >= 3, b.Summary);
b.ApplyCommand.Execute(null); Wait(() => !b.IsBusy); Pump(20);
Check("modo legal pula os ilegais", CoreAdapter.GetBoxSlot(Sav(), 0, 0).Ball != (byte)Ball.Master, vm.Status);
vm.LegalMode = false;
b.ApplyCommand.Execute(null);
Check("sem modo legal grava mesmo ilegal", Wait(() => CoreAdapter.GetBoxSlot(Sav(), 0, 0).Ball == (byte)Ball.Master), vm.Status);
vm.UndoCommand.Execute(null); Pump();
vm.LegalMode = true;

// Cancelar a pergunta não grava
answer = false;
b.Script = ".CurrentLevel=77";
b.ApplyCommand.Execute(null); Wait(() => vm.Status.Contains("cancelada"));
Check("cancelar não grava", Level(0, 0) != 77, vm.Status);
answer = true;

// Selecionados vazio
b.Script = "";
b.Actions.First(a => a.Name == "IVs máximos").IsChecked = true;
b.ScopeIndex = (int)BatchScope.Marked;
b.PreviewCommand.Execute(null); Wait(() => !b.IsBusy);
Check("selecionados vazio explica o que fazer", b.HasError && b.Error.Contains("Ctrl+clique"), b.Error);

// Resultados da Pesquisa
vm.CurrentPage = vm.Search; Wait(() => !vm.Search.IsBusy && vm.Search.Results.Count > 0);
vm.Search.Query = "zorua"; Pump();
vm.CurrentPage = b; Pump();
b.ScopeIndex = (int)BatchScope.SearchResults;
b.PreviewCommand.Execute(null); Wait(() => !b.IsBusy);
Check("resultados da Pesquisa = Zorua", b.Preview.Count == 1 && b.Preview[0].Name == "Zorua", b.Summary);
int Ivs() { var z = CoreAdapter.GetBoxSlot(Sav(), 1, 0); return z.IV_HP + z.IV_ATK + z.IV_DEF + z.IV_SPA + z.IV_SPD + z.IV_SPE; }
int ivBefore = Ivs();
b.ApplyCommand.Execute(null);
Check("IVs máximos aplicados no Zorua", Wait(() => vm.Status.Contains("alterados")) && ivBefore < 186 && Ivs() == 186, $"{ivBefore} {vm.Status}");
Shot("batch");

// Legalizar em lote: só o ilegal muda, e Aplicar grava exatamente o que foi pré-visualizado
foreach (var a in b.Actions) a.IsChecked = false;
b.Script = "";
Check("ação Pokébola legal (sem o nome antigo)", b.Actions.Any(a => a.Name == "Pokébola legal") && b.Actions.All(a => a.Name != "Bola legal"));
Check("Lillipup quebrado está ilegal", CoreAdapter.IsLegal(CoreAdapter.GetBoxSlot(Sav(), 2, 0)) == false);
b.Actions.First(a => a.Name == "Legalizar").IsChecked = true;
b.ScopeIndex = (int)BatchScope.AllBoxes;
b.PreviewCommand.Execute(null);
Check("pré-visualização: só o ilegal é legalizado", Wait(() => !b.IsBusy && b.Preview.Count == 1, 60000) && b.Preview[0].Name == "Lillipup" && b.Preview[0].Diff.Contains("legalizado"), b.Summary);
Pump(20);
Check("resumo final não é sobrescrito pelo progresso", b.Summary.Contains("1 legalizados"), b.Summary);
uint previewedPid = b.Preview.Count == 1 ? b.Preview[0].Change.Result.PID : 0;
b.ApplyCommand.Execute(null);
Check("legalizar em lote grava o Pokémon legal", Wait(() => CoreAdapter.IsLegal(CoreAdapter.GetBoxSlot(Sav(), 2, 0)) == true, 60000), vm.Status);
Check("Aplicar grava exatamente o pré-visualizado (sem gerar de novo)", CoreAdapter.GetBoxSlot(Sav(), 2, 0).PID == previewedPid);
vm.UndoCommand.Execute(null); Pump();
Check("Ctrl+Z desfaz o legalizar em lote", CoreAdapter.IsLegal(CoreAdapter.GetBoxSlot(Sav(), 2, 0)) == false);

Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
