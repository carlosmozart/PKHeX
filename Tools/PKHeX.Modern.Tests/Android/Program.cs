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
void Check(string label, bool ok) { Console.WriteLine((ok ? "OK " : "FAIL ") + label); if (!ok) failures++; }
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
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "OpenFileBookmarkAsync" ? Task.FromResult(File) : throw new NotSupportedException();
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
    public Task<Stream> OpenReadAsync() => Task.FromResult<Stream>(new MemoryStream(Bytes));
    public Task<Stream> OpenWriteAsync()
    {
        if (ReadOnly) throw new IOException("somente leitura");
        var corrupt = CorruptWriteOnce; CorruptWriteOnce = false;
        return Task.FromResult<Stream>(new CommitStream(data => Bytes = corrupt ? [0] : data));
    }
    public Task<StorageItemProperties> GetBasicPropertiesAsync() => Task.FromResult(new StorageItemProperties());
    public Task<IStorageFolder?> GetParentAsync() => Task.FromResult<IStorageFolder?>(null);
    public Task DeleteAsync() => Task.CompletedTask;
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
