using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
int fails = 0, questions = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
void Wait(Task task, MainViewModel? vm = null, bool answer = true)
{
    var end = DateTime.UtcNow.AddMinutes(2);
    while (!task.IsCompleted && DateTime.UtcNow < end)
    {
        if (vm?.Dialog is { } dialog) { questions++; dialog.Complete(answer); }
        Pump(); System.Threading.Thread.Sleep(5);
    }
    task.GetAwaiter().GetResult();
}
var sourcePath = Path.Combine(work, "fr.sav"); var source = CoreAdapter.LoadSave(sourcePath)!; CoreAdapter.Activate(source); source.ClearBoxes();
foreach (ushort species in new ushort[] { 25, 133 })
{
    var pk = EncounterDatabase.SearchEncounters(source, species, true).Where(e => e.Species == species)
        .Select(e => EncounterDatabase.ToEntity(source, e, out _)).First(p => p is not null && new LegalityAnalysis(p).Valid)!;
    source.SetBoxSlotAtIndex(pk, 0, species == 25 ? 0 : 1);
}
File.WriteAllBytes(sourcePath, source.Write().ToArray());
var vm = new MainViewModel(new AppSettings { LegalMode = false }); vm.Open(sourcePath); vm.CurrentPage = vm.Boxes;
var folder = Path.Combine(work, "export"); int count = vm.ExportBoxesToFolder(folder);
var exported = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
Check("exporta todas em subpastas", count > 0 && count == exported.Length && Directory.GetDirectories(folder).Length > 0);
Check("exportar novamente preserva arquivos", vm.ExportBoxesToFolder(folder) == 0);

var copy = CoreAdapter.LoadSave(Path.Combine(work, "fr.sav"))!; copy.ClearBoxes();
var target = Path.Combine(work, "target.sav"); File.WriteAllBytes(target, copy.Write().ToArray());
vm.Open(target); vm.CurrentPage = vm.Boxes;
Wait(vm.ImportBoxFolderAsync(exported), vm);
Check("importa mesma geração", vm.Status == $"Importados: {count}; recusados: 0." && vm.IsDirty);
Check("exporta alteração e reabre", vm.Export(target) && CoreAdapter.LoadSave(target)!.ChecksumsValid);
var after = CoreAdapter.LoadSave(target)!;
Check("contagem no save", after.BoxData.Count(pk => pk.Species > 0) == count);
vm.UndoCommand.Execute(null); Check("desfaz o lote", vm.Boxes.Slots.All(s => s.IsEmpty) && !vm.UndoCommand.CanExecute(null));
vm.RedoCommand.Execute(null); Check("refaz o lote", vm.Boxes.Slots.Any(s => !s.IsEmpty));
vm.ClearImportBoxes = true; questions = 0;
Wait(vm.ImportBoxFolderAsync(exported), vm, answer: false);
Check("cancelar limpeza preserva caixas", questions == 1 && vm.Boxes.Slots.Any(s => !s.IsEmpty));

vm.Open(Path.Combine(work, "moon.sav")); vm.CurrentPage = vm.Boxes; vm.ClearImportBoxes = true; vm.ImportFirstBox = true; questions = 0;
Wait(vm.ImportBoxFolderAsync(exported), vm);
Check("converte Gen3 para Gen7", vm.Status.StartsWith($"Importados: {count};") && questions == 1);
var moonOut = Path.Combine(work, "moon-out.sav"); vm.Export(moonOut);
var moon = CoreAdapter.LoadSave(moonOut)!;
Check("conversão reabre com checksum", moon.ChecksumsValid && moon.BoxData.Count(pk => pk.Species > 0) == count && moon.BoxData.Where(pk => pk.Species > 0).All(pk => pk is PK7));
var future = moon.BoxData.First(pk => pk.Species > 0); var pk7 = Path.Combine(work, "future.pk7"); CoreAdapter.ExportEntity(future, pk7);
vm.Open(target); questions = 0; Wait(vm.ImportBoxFolderAsync([pk7]), vm);
Check("recusa geração futura com motivo", vm.Status == "Importados: 0; recusados: 1." && questions == 1);

var remote = new StorageNode("content://test/tree/boxes", "boxes", true);
var writeTask = BoxFolderTransfer.WriteAsync(folder, remote.Folder); Wait(writeTask); int written = writeTask.Result;
Check("SAF exporta sem caminho local", written == count && remote.Folder.TryGetLocalPath() is null);
var repeatTask = BoxFolderTransfer.WriteAsync(folder, remote.Folder); Wait(repeatTask); Check("SAF exportação não sobrescreve", repeatTask.Result == 0);
var readTask = BoxFolderTransfer.ReadAsync(remote.Folder, Path.Combine(work, "saf-read")); Wait(readTask); var readFiles = readTask.Result;
Check("SAF lê subpastas e bytes idênticos", readFiles.Count == count && readFiles.Select(File.ReadAllBytes).All(b => exported.Any(p => File.ReadAllBytes(p).SequenceEqual(b))));

// Captura do menu com save sintetico.
var demo = BlankSaveFile.Get(GameVersion.B); var demoPath = Path.Combine(work, "demo.sav"); File.WriteAllBytes(demoPath, demo.Write().ToArray());
vm.Open(demoPath); vm.CurrentPage = vm.Boxes;
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 950 }; win.Show(); for (int i = 0; i < 10; i++) Pump();
var menu = win.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BoxFolderButton"); menu.Flyout!.ShowAt(menu); for (int i = 0; i < 10; i++) Pump();
win.CaptureRenderedFrame()!.Save(Path.Combine(work, "box-folders.png")); menu.Flyout.Hide(); win.Close();
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;

public sealed class StorageNode
{
    public string Name; public Uri Uri; public byte[] Bytes = []; public List<StorageNode> Children = [];
    public IStorageItem Handle;
    public IStorageFolder Folder => (IStorageFolder)Handle;
    public StorageNode(string uri, string name, bool folder)
    {
        Name = name; Uri = new Uri(uri);
        Handle = folder ? DispatchProxy.Create<IStorageFolder, NodeProxy>() : DispatchProxy.Create<IStorageFile, NodeProxy>();
        ((NodeProxy)(object)Handle).Node = this;
    }
    public StorageNode Child(string name, bool folder)
    {
        var node = Children.FirstOrDefault(n => n.Name == name);
        if (node is not null) return node;
        node = new StorageNode(Uri + "/" + Uri.EscapeDataString(name), name, folder); Children.Add(node); return node;
    }
    public async IAsyncEnumerable<IStorageItem> Items() { foreach (var n in Children) { await Task.Yield(); yield return n.Handle; } }
}
public class NodeProxy : DispatchProxy
{
    public StorageNode Node = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Name" => Node.Name, "get_Path" => Node.Uri, "get_CanBookmark" => false,
        "GetItemsAsync" => Node.Items(),
        "GetFileAsync" => Task.FromResult(Node.Children.FirstOrDefault(n => n.Name == (string)args![0]! && n.Handle is IStorageFile)?.Handle as IStorageFile),
        "GetFolderAsync" => Task.FromResult(Node.Children.FirstOrDefault(n => n.Name == (string)args![0]! && n.Handle is IStorageFolder)?.Handle as IStorageFolder),
        "CreateFileAsync" => Task.FromResult<IStorageFile?>((IStorageFile)Node.Child((string)args![0]!, false).Handle),
        "CreateFolderAsync" => Task.FromResult<IStorageFolder?>(Node.Child((string)args![0]!, true).Folder),
        "OpenReadAsync" => Task.FromResult<Stream>(new MemoryStream(Node.Bytes)),
        "OpenWriteAsync" => Task.FromResult<Stream>(new CommitStream(b => Node.Bytes = b)),
        "Dispose" => null, _ => throw new NotSupportedException(method?.Name)
    };
}
public sealed class CommitStream(Action<byte[]> commit) : MemoryStream
{
    protected override void Dispose(bool disposing) { if (disposing) commit(ToArray()); base.Dispose(disposing); }
    public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
