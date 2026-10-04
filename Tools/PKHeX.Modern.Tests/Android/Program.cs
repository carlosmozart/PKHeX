using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;
using System.IO.Compression;
using System.Reflection;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var artifactRoot = args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "pkhex-android-tests");
var work = Path.Combine(artifactRoot, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
SaveBackup.Folder = Path.Combine(work, "backups"); BankStorage.Root = Path.Combine(work, "bank");
int failures = 0;
void Check(string label, bool ok, string extra = "") { Console.WriteLine((ok ? "OK " : "FAIL ") + label + (ok || extra.Length == 0 ? "" : " -> " + extra)); if (!ok) failures++; }
void Wait(Task task)
{
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (!task.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
    task.GetAwaiter().GetResult();
}
T Await<T>(Task<T> task) { Wait(task); return task.Result; }
bool Throws(Func<Task> action) { try { Wait(action()); return false; } catch (IOException) { return true; } }
var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "ANDROID";
CoreAdapter.Activate(sav); var pk = EncounterDatabase.SearchEncounters(sav, 25, true).Where(e => e.Species == 25).Select(e => EncounterDatabase.ToEntity(sav, e, out _)).First(p => p is not null && new LegalityAnalysis(p).Valid)!; sav.SetBoxSlotAtIndex(pk, 0, 0); sav.SetPartySlotAtIndex(pk.Clone(), 0);
var original = sav.Write().ToArray(); File.WriteAllBytes(Path.Combine(artifactRoot, "teste-android-black.sav"), original); Check("amostra sintetica tem Pikachu legal", new LegalityAnalysis(pk).Valid);
var file = new FakeFile("content://test/provider/one", "main.sav", original);
var documents = new MobileDocuments(Path.Combine(work, "private"));
var doc = Await(documents.ImportAsync(file.Handle));
Check("content URI ganha copia privada, sem caminho externo", File.Exists(doc.Path) && !doc.Path.Contains("content:"));
var sameName = new FakeFile("content://test/provider/two", "main.sav", original);
var second = Await(documents.ImportAsync(sameName.Handle));
Check("nomes iguais nao colidem", doc.Path != second.Path && doc.Id != second.Id);
var vm = new MainViewModel(new AppSettings { LegalMode = false }); vm.Open(doc.Path); vm.CurrentPage = vm.Boxes;
vm.MoveSlot(vm.Boxes.Slots[0], vm.Boxes.Slots[1], false);
Check("movimento marca alteracoes", vm.IsDirty && vm.Boxes.Slots[0].IsEmpty && vm.Boxes.Slots[1].Title == "Pikachu");
var staging = Path.Combine(work, "staging.sav");
Check("exportacao de preparo mantem alteracoes pendentes", vm.Export(staging, false) && vm.IsDirty);
var changed = File.ReadAllBytes(staging);
var copyTarget = new FakeFile("content://test/copy", "copy.sav", original);
Wait(documents.ExportCopyAsync(copyTarget.Handle, [1,2,3]));
Check("exportacao menor trunca o destino", copyTarget.Bytes.SequenceEqual(new byte[] { 1,2,3 }));
Wait(documents.SaveAsync(doc, changed, null!));
Check("documento gravado e conferido", file.Bytes.SequenceEqual(changed));
Check("backup contem original completo", File.ReadAllBytes(documents.Backups(doc).Single()).SequenceEqual(original));
vm.MarkExternallySaved(); Check("somente confirmacao externa limpa pendencias", !vm.IsDirty);
doc = documents.Find(doc.Path)!;
file.Bytes = original;
Check("alteracao externa bloqueia sobrescrita", Throws(() => documents.SaveAsync(doc, changed, null!)) && file.Bytes.SequenceEqual(original));
file.Bytes = changed; file.ReadOnly = true;
Check("somente leitura preserva original", Throws(() => documents.SaveAsync(doc, original, null!)) && file.Bytes.SequenceEqual(changed));
file.ReadOnly = false; file.CorruptWriteOnce = true;
Check("gravacao corrompida e detectada e restaura original", Throws(() => documents.SaveAsync(doc, original, null!)) && file.Bytes.SequenceEqual(changed));
var restarted = new MobileDocuments(Path.Combine(work, "private"));
var provider = DispatchProxy.Create<IStorageProvider, ProviderProxy>();
((ProviderProxy)(object)provider).File = file.Handle;
Wait(restarted.SaveAsync(restarted.Find(doc.Path)!, original, provider));
Check("bookmark restaura acesso depois de reiniciar", file.Bytes.SequenceEqual(original));
var denied = DispatchProxy.Create<IStorageProvider, ProviderProxy>();
var lost = new MobileDocuments(Path.Combine(work, "private"));
Check("bookmark revogado bloqueia escrita", Throws(() => lost.SaveAsync(lost.Find(doc.Path)!, changed, denied)));
Check("arquivo invalido nao entra na biblioteca", Throws(async () => { await documents.ImportAsync(new FakeFile("content://test/invalid", "bad.sav", [1,2,3]).Handle); }));
var zipPath = Path.Combine(work, "source.zip");
using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
{
    using (var output = zip.CreateEntry("nested/main").Open()) output.Write(original);
    using (var output = zip.CreateEntry("notes.txt").Open()) output.Write("preservar"u8);
}
var zipFile = new FakeFile("content://test/zip", "backup.zip", File.ReadAllBytes(zipPath));
var zipDoc = Await(documents.ImportAsync(zipFile.Handle));
var entry = ZipSaves.ReadAll(zipDoc.Path).Single().Path;
vm.Open(entry); vm.CurrentPage = vm.Boxes; vm.MoveSlot(vm.Boxes.Slots[0], vm.Boxes.Slots[2], false);
Check("ZIP exportado sem limpar pendencias", vm.Export(entry, false) && vm.IsDirty);
Wait(documents.SaveAsync(zipDoc, File.ReadAllBytes(zipDoc.Path), null!));
using (var zip = new ZipArchive(new MemoryStream(zipFile.Bytes)))
using (var reader = new StreamReader(zip.GetEntry("notes.txt")!.Open())) Check("ZIP preserva entradas alheias", reader.ReadToEnd() == "preservar");
// Bank em pasta externa: pasta do seletor (content://) com copia privada sincronizada.
var pkFile = Path.Combine(work, "pika.pk5"); CoreAdapter.ExportEntity(pk, pkFile); var pkBytes = File.ReadAllBytes(pkFile);
var remoteFolder = new FakeFolder("content://test/tree/pkm", "Meus Pokemon");
remoteFolder.Add("a.pk5", pkBytes); remoteFolder.Add("b.pk5", pkBytes); remoteFolder.Add("notas.txt", "x"u8.ToArray());
var folderProvider = DispatchProxy.Create<IStorageProvider, ProviderProxy>(); ((ProviderProxy)(object)folderProvider).Folder = remoteFolder.Handle;
var statusLog = new List<string>();
var bankFolders = new MobileBankFolders(Path.Combine(work, "private"), statusLog.Add);
var mirror = Await(bankFolders.AddAsync(folderProvider))!;
Check("pasta externa vira copia privada com os .pk*", bankFolders.IsMirror(mirror) && Directory.GetFiles(mirror).Select(Path.GetFileName).Order().SequenceEqual(["a.pk5", "b.pk5"]));
Check("nome da pasta original nas mensagens", bankFolders.Describe(mirror) == "Meus Pokemon" && BankStorage.GetExternalBankName(mirror) == "📁 Meus Pokemon");
BankStorage.ExternalFolders = [mirror];
var extBox = new BankBox(mirror, "Caixa 1", 0);
BankStorage.WriteSlot(extBox, 2, pk); Wait(bankFolders.PushAsync(mirror, folderProvider));
Check("Pokemon gravado no Bank vai para a pasta", remoteFolder.Files.Count(f => f.Name.EndsWith(".pk5")) == 3);
BankStorage.DeleteSlot(extBox, 0); Wait(bankFolders.PushAsync(mirror, folderProvider));
Check("Pokemon apagado no Bank sai da pasta", remoteFolder.Files.All(f => f.Name != "a.pk5") && remoteFolder.Files.Any(f => f.Name == "b.pk5"));
var changedPk = pkBytes.ToArray(); changedPk[^1] ^= 0xFF; remoteFolder.Get("b.pk5")!.Bytes = changedPk;
remoteFolder.Add("c.pk5", pkBytes);
Wait(bankFolders.SyncAllAsync(folderProvider));
Check("mudanca feita fora do app chega na copia", File.ReadAllBytes(Path.Combine(mirror, "b.pk5")).SequenceEqual(changedPk) && File.Exists(Path.Combine(mirror, "c.pk5")));
remoteFolder.Remove("c.pk5"); Wait(bankFolders.SyncAllAsync(folderProvider));
Check("arquivo apagado fora do app sai da copia", !File.Exists(Path.Combine(mirror, "c.pk5")));
File.WriteAllBytes(Path.Combine(mirror, "b.pk5"), pkBytes); remoteFolder.Get("b.pk5")!.Bytes = [.. changedPk.Reverse()];
Wait(bankFolders.SyncAllAsync(folderProvider));
Check("conflito: vale a pasta e a versao do app vai para os backups", File.ReadAllBytes(Path.Combine(mirror, "b.pk5")).SequenceEqual(changedPk.Reverse()) && statusLog.Any(m => m.Contains("backups")));
Check("arquivos que nao sao Pokemon ficam fora", remoteFolder.Get("notas.txt") is not null && !File.Exists(Path.Combine(mirror, "notas.txt")));
var reopened = new MobileBankFolders(Path.Combine(work, "private"), statusLog.Add);
remoteFolder.Add("d.pk5", pkBytes); Wait(reopened.SyncAllAsync(folderProvider));
Check("depois de reiniciar, o acesso volta pelo bookmark", File.Exists(Path.Combine(mirror, "d.pk5")));
reopened.Remove(mirror);
Check("remover a pasta apaga so a copia", !Directory.Exists(mirror) && remoteFolder.Files.Count >= 3);
BankStorage.ExternalFolders = [];
// Pasta de saves: a pasta do seletor (com subpastas) aparece na pagina Saves e cada save grava de volta no original.
var saveRoot = Path.Combine(work, "savefolder");
var savesTree = new FakeFolder("content://test/tree/saves", "Saves");
savesTree.Add("main.sav", original);
var sub = savesTree.AddFolder("Pokemon Black 2");
sub.Add("outro.sav", original);
var treeProvider = DispatchProxy.Create<IStorageProvider, ProviderProxy>(); ((ProviderProxy)(object)treeProvider).Folder = savesTree.Handle;
var saveDocs = new MobileDocuments(saveRoot);
var saveFolder = new MobileSaveFolder(saveRoot, saveDocs);
var saveCount = Await(saveFolder.ChooseAsync(treeProvider));
var listed = SaveLibrary.Scan(Path.Combine(saveRoot, "documents"), out _);
Check("pasta de saves lida com subpastas", saveCount == 2 && listed.Count == 2 && saveFolder.FolderName == "Saves");
Check("resumo da leitura da pasta", saveFolder.LastSummary.Contains("2 arquivo(s), 2 lido(s)"), saveFolder.LastSummary);
var broken = savesTree.Add("quebrado.sav", original); broken.BrokenRead = true;
var good = sub.Add("novo.sav", original);
var withBroken = Await(saveFolder.SyncAsync(treeProvider));
Check("arquivo com erro nao impede os outros", saveDocs.FolderDocuments.Any(d => d.Relative == "Pokemon Black 2/novo.sav") && saveFolder.LastSummary.Contains("1 com erro"), saveFolder.LastSummary);
savesTree.Remove("quebrado.sav"); sub.Remove("novo.sav"); Wait(saveFolder.SyncAsync(treeProvider));
var mainLocal = saveDocs.FolderDocuments.Single(d => d.Relative == "main.sav").Path;
var subLocal = saveDocs.FolderDocuments.Single(d => d.Relative == "Pokemon Black 2/outro.sav").Path;
savesTree.Get("main.sav")!.Bytes = changed;
Wait(saveFolder.SyncAsync(treeProvider));
Check("save mudado na pasta chega ao atualizar", File.ReadAllBytes(mainLocal).SequenceEqual(changed));
Wait(saveDocs.SaveAsync(saveDocs.Find(mainLocal)!, original, treeProvider));
Check("Salvar grava no arquivo da pasta", savesTree.Get("main.sav")!.Bytes.SequenceEqual(original));
var restartedDocs = new MobileDocuments(saveRoot);
var restartedFolder = new MobileSaveFolder(saveRoot, restartedDocs);
File.WriteAllBytes(subLocal, changed);
Wait(restartedDocs.SaveAsync(restartedDocs.Find(subLocal)!, changed, treeProvider));
Check("depois de reabrir, Salvar acha o original pela pasta", sub.Get("outro.sav")!.Bytes.SequenceEqual(changed));
File.WriteAllBytes(mainLocal, changed); // alteracao no app ainda nao salva
savesTree.Get("main.sav")!.Bytes = [.. original.Reverse()];
Wait(restartedFolder.SyncAsync(treeProvider));
Check("save com alteracoes no app nao e trocado pela pasta", File.ReadAllBytes(mainLocal).SequenceEqual(changed));
sub.Remove("outro.sav");
Wait(restartedFolder.SyncAsync(treeProvider));
Check("save apagado na pasta sai da lista", !File.Exists(subLocal) && restartedDocs.FolderDocuments.All(d => d.Relative != "Pokemon Black 2/outro.sav"));
// Telefone em paisagem (S24+: 892x412 dp): a interface do desktop inteira, reduzida para caber.
var mobile = new MobileShell(vm);
var window = new Window { Width = 892, Height = 412, Content = mobile }; window.Show(); Dispatcher.UIThread.RunJobs();
Check("escala de telefone", Math.Abs(MobileShell.ScaleFor(new Size(892, 412)) - 412 / MobileShell.DesignHeight) < 0.001);
Check("tablet sem reducao", MobileShell.ScaleFor(new Size(1280, 800)) == 1);
Check("interface do desktop no Android", mobile.View.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "SearchBox"));
vm.CurrentPage = vm.Boxes; Dispatcher.UIThread.RunJobs();
var slotButton = mobile.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is SlotViewModel { IsEmpty: false });
Wait(vm.SelectSlotAsync((SlotViewModel)slotButton.DataContext!)); Dispatcher.UIThread.RunJobs();
Check("editor ao lado das caixas", mobile.GetVisualDescendants().OfType<PokemonEditorView>().Any());
foreach (var page in vm.Pages.Where(p => p != vm.SaveManager && p != vm.Batch))
{
    vm.CurrentPage = page; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    Check("pagina cabe na tela: " + page.Title, mobile.View.Bounds.Width >= MobileShell.DesignWidth - 1 && mobile.Bounds.Width <= 892 && mobile.Bounds.Height <= 412);
}
vm.CurrentPage = vm.Boxes; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
window.CaptureRenderedFrame()?.Save(Path.Combine(artifactRoot, "android-landscape.png"));
// Fontes de simbolos do Android (MainActivity.FontFallbacks): os nomes precisam achar os arquivos embutidos.
foreach (var (family, text) in new[] { ("Noto Sans Symbols 2", "⭳⭱⌨◐"), ("Noto Emoji", "🗑💾❔🔍♂♀") })
{
    var tf = new Avalonia.Media.Typeface(new Avalonia.Media.FontFamily("avares://PKHeX.Modern.UI/Assets/Fonts#" + family));
    var ok = Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(tf, out var glyphs) && glyphs!.FamilyName == family;
    var missing = ok ? string.Concat(text.EnumerateRunes().Where(r => !glyphs!.TryGetGlyph((uint)r.Value, out var g) || g == 0)) : "(fonte nao carregou)";
    Check($"fonte embutida {family} {missing}", missing.Length == 0);
}
window.Close();
Console.WriteLine(failures == 0 ? "TUDO OK" : $"{failures} falhas");
return failures == 0 ? 0 : 1;

public class ProviderProxy : DispatchProxy
{
    public IStorageBookmarkFile? File;
    public IStorageBookmarkFolder? Folder;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "OpenFileBookmarkAsync" => Task.FromResult(File),
        "OpenFolderBookmarkAsync" => Task.FromResult(Folder),
        "OpenFolderPickerAsync" => Task.FromResult<IReadOnlyList<IStorageFolder>>(Folder is null ? [] : [Folder]),
        _ => throw new NotSupportedException(method?.Name),
    };
}
public sealed class FakeFile
{
    private readonly string uri; private readonly string name; public byte[] Bytes; public IStorageBookmarkFile Handle { get; } public FakeFile(string uri, string name, byte[] bytes) { this.uri = uri; this.name = name; Bytes = bytes; Handle = DispatchProxy.Create<IStorageBookmarkFile, FileProxy>(); ((FileProxy)(object)Handle).State = this; }
    public bool ReadOnly;
    public bool CorruptWriteOnce;
    public string Name => name;
    public Uri Path => new(uri);
    public bool CanBookmark => true;
    public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>(uri);
    public bool BrokenRead;
    public Task<Stream> OpenReadAsync() => BrokenRead ? Task.FromException<Stream>(new IOException("leitura negada")) : Task.FromResult<Stream>(new MemoryStream(Bytes));
    public Task<Stream> OpenWriteAsync()
    {
        if (ReadOnly) throw new IOException("somente leitura");
        var corrupt = CorruptWriteOnce; CorruptWriteOnce = false;
        return Task.FromResult<Stream>(new CommitStream(data => Bytes = corrupt ? [0] : data));
    }
    public Task<StorageItemProperties> GetBasicPropertiesAsync() => Task.FromResult(new StorageItemProperties());
    public Task<IStorageFolder?> GetParentAsync() => Task.FromResult<IStorageFolder?>(null);
    public FakeFolder? Parent;
    public Task DeleteAsync() { Parent?.Files.Remove(this); return Task.CompletedTask; }
    public Task<IStorageItem?> MoveAsync(IStorageFolder folder) => Task.FromResult<IStorageItem?>(null);
    public Task ReleaseBookmarkAsync() => Task.CompletedTask;
    public void Dispose() { }
}
public sealed class CommitStream(Action<byte[]> commit) : MemoryStream
{
    protected override void Dispose(bool disposing) { if (disposing) commit(ToArray()); base.Dispose(disposing); }
    public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}

public class FileProxy : DispatchProxy
{
    public FakeFile State = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Name" => State.Name, "get_Path" => State.Path, "get_CanBookmark" => true,
        "OpenReadAsync" => State.OpenReadAsync(), "OpenWriteAsync" => State.OpenWriteAsync(),
        "SaveBookmarkAsync" => State.SaveBookmarkAsync(), "GetBasicPropertiesAsync" => State.GetBasicPropertiesAsync(),
        "GetParentAsync" => State.GetParentAsync(), "DeleteAsync" => State.DeleteAsync(),
        "MoveAsync" => State.MoveAsync((IStorageFolder)args![0]!), "ReleaseBookmarkAsync" => State.ReleaseBookmarkAsync(),
        "Dispose" => null, _ => throw new NotSupportedException(method?.Name)
    };
}

public sealed class FakeFolder
{
    public readonly List<FakeFile> Files = [];
    public string Uri { get; } public string Name { get; } public IStorageBookmarkFolder Handle { get; }
    public FakeFolder(string uri, string name) { Uri = uri; Name = name; Handle = DispatchProxy.Create<IStorageBookmarkFolder, FolderProxy>(); ((FolderProxy)(object)Handle).State = this; }
    public FakeFile Add(string name, byte[] bytes) { var f = new FakeFile(Uri + "/" + name, name, bytes) { Parent = this }; Files.Add(f); return f; }
    public readonly List<FakeFolder> Folders = [];
    public FakeFolder AddFolder(string name) { var f = new FakeFolder(Uri + "/" + name, name); Folders.Add(f); return f; }
    public FakeFile? Get(string name) => Files.FirstOrDefault(f => f.Name == name);
    public void Remove(string name) => Files.RemoveAll(f => f.Name == name);
    public async IAsyncEnumerable<IStorageItem> Items()
    {
        foreach (var f in Files.ToArray()) { await Task.Yield(); yield return f.Handle; }
        foreach (var d in Folders.ToArray()) { await Task.Yield(); yield return d.Handle; }
    }
}
public class FolderProxy : DispatchProxy
{
    public FakeFolder State = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Name" => State.Name, "get_Path" => new Uri(State.Uri), "get_CanBookmark" => true,
        "SaveBookmarkAsync" => Task.FromResult<string?>(State.Uri),
        "GetItemsAsync" => State.Items(),
        "CreateFileAsync" => Task.FromResult<IStorageFile?>(State.Add((string)args![0]!, []).Handle),
        "GetBasicPropertiesAsync" => Task.FromResult(new StorageItemProperties()),
        "GetFolderAsync" => Task.FromResult<IStorageFolder?>(State.Folders.FirstOrDefault(f => f.Name == (string)args![0]!)?.Handle),
        "GetFileAsync" => Task.FromResult<IStorageFile?>(State.Get((string)args![0]!)?.Handle),
        "Dispose" => null, _ => throw new NotSupportedException(method?.Name)
    };
}
