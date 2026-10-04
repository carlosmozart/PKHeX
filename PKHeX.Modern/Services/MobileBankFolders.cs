using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>
/// Bank em pasta externa no Android. A pasta escolhida no seletor (content://) nao tem caminho local: o app guarda uma
/// copia privada em external-banks/&lt;id&gt;/&lt;nome&gt;, que o BankStorage usa como uma pasta externa comum, e sincroniza:
/// ao abrir o app traz o que mudou na pasta; cada gravacao ou exclusao no Bank vai de volta para a pasta original.
/// Um arquivo alterado nos dois lados fica com a versao da pasta; a do app vai para os backups.
/// </summary>
public sealed class MobileBankFolders
{
    public sealed class Folder
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Bookmark { get; set; } = "";
        /// <summary>Arquivo → SHA-256 do conteudo na ultima sincronizacao (igual nos dois lados).</summary>
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly string _root;
    private readonly string _index;
    private readonly Dictionary<string, Folder> _folders;
    private readonly Dictionary<string, IStorageFolder> _handles = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Action<string> _status;
    private static readonly HashSet<string> Extensions = new(EntityFileExtension.GetExtensionsAll().Select(e => "." + e), StringComparer.OrdinalIgnoreCase);

    public MobileBankFolders(string root, Action<string> status)
    {
        _root = Path.Combine(root, "external-banks");
        _index = Path.Combine(root, "external-banks.json");
        _status = status;
        Directory.CreateDirectory(_root);
        _folders = File.Exists(_index)
            ? JsonSerializer.Deserialize<Dictionary<string, Folder>>(File.ReadAllText(_index)) ?? new() : new();
    }

    /// <summary>Copias privadas registradas (os caminhos que entram em ExternalBankFolders).</summary>
    public IEnumerable<string> LocalPaths => _folders.Values.Select(LocalPath);

    public bool IsMirror(string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>Nome da pasta original (as mensagens do Bank mostram este, nao o caminho da copia).</summary>
    public string Describe(string path) => Find(path)?.Name ?? path;

    private string LocalPath(Folder f) => Path.Combine(_root, f.Id, f.Name);

    private Folder? Find(string path) => _folders.Values.FirstOrDefault(f => string.Equals(Path.GetFullPath(LocalPath(f)), Path.GetFullPath(path), StringComparison.Ordinal));

    private void Persist()
    {
        File.WriteAllText(_index + ".tmp", JsonSerializer.Serialize(_folders));
        File.Move(_index + ".tmp", _index, true);
    }

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static string SafeName(string name)
    {
        name = Path.GetFileName(name.Replace('\\', '/'));
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) || name is "." or ".." ? "Pasta" : name;
    }

    /// <summary>Escolhe a pasta no seletor, guarda o acesso e traz os arquivos. Retorna o caminho da copia (ou nulo se cancelou).</summary>
    public async Task<string?> AddAsync(IStorageProvider provider)
    {
        var picked = (await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta com arquivos .pk* para usar como banco", AllowMultiple = false })).FirstOrDefault();
        if (picked is null)
            return null;
        var bookmark = await picked.SaveBookmarkAsync() ?? throw new IOException("O Android não deixou guardar o acesso a esta pasta.");
        var id = Hash(System.Text.Encoding.UTF8.GetBytes(picked.Path.ToString()))[..16];
        if (!_folders.TryGetValue(id, out var folder))
            _folders[id] = folder = new Folder { Id = id, Name = SafeName(picked.Name), Bookmark = bookmark };
        else
            folder.Bookmark = bookmark;
        _handles[id] = picked;
        Persist();
        await SyncAsync(folder, provider);
        return LocalPath(folder);
    }

    /// <summary>Pasta saiu da lista do Bank: apaga a copia privada (os arquivos da pasta original ficam).</summary>
    public void Remove(string path)
    {
        if (Find(path) is not { } folder)
            return;
        _folders.Remove(folder.Id);
        _handles.Remove(folder.Id);
        Persist();
        try { Directory.Delete(Path.Combine(_root, folder.Id), true); } catch (IOException) { }
    }

    /// <summary>Sincroniza todas as pastas (ao abrir o app).</summary>
    public async Task SyncAllAsync(IStorageProvider provider)
    {
        foreach (var folder in _folders.Values.ToArray())
            await SyncAsync(folder, provider);
    }

    /// <summary>Uma gravacao do Bank mudou a copia: leva a mudanca para a pasta original.</summary>
    public async Task PushAsync(string path, IStorageProvider provider)
    {
        if (Find(path) is { } folder)
            await SyncAsync(folder, provider);
    }

    private async Task<IStorageFolder?> OpenAsync(Folder folder, IStorageProvider provider)
    {
        if (_handles.TryGetValue(folder.Id, out var handle))
            return handle;
        handle = await provider.OpenFolderBookmarkAsync(folder.Bookmark);
        if (handle is not null)
            _handles[folder.Id] = handle;
        return handle;
    }

    /// <summary>
    /// Traz da pasta o que mudou la (arquivo sem mudanca no app) e leva para a pasta o que mudou no app.
    /// Falhas viram mensagem de status; a copia privada nunca e apagada por um erro de acesso.
    /// </summary>
    private async Task SyncAsync(Folder folder, IStorageProvider provider)
    {
        await _lock.WaitAsync();
        try
        {
            var remoteFolder = await OpenAsync(folder, provider);
            if (remoteFolder is null)
            {
                _status($"Sem acesso à pasta {folder.Name}. Remova e adicione a pasta de novo no Bank.");
                return;
            }
            var local = LocalPath(folder);
            Directory.CreateDirectory(local);

            // Pasta original: arquivos .pk* com conteudo e data (a ordem das caixas segue a data, depois o nome).
            var remote = new Dictionary<string, (IStorageFile File, byte[] Data, DateTimeOffset When)>(StringComparer.OrdinalIgnoreCase);
            await foreach (var item in remoteFolder.GetItemsAsync())
            {
                if (item is not IStorageFile file || !Extensions.Contains(Path.GetExtension(file.Name)))
                    continue;
                await using var input = await file.OpenReadAsync();
                var data = await MobileDocuments.ReadBoundedAsync(input);
                var props = await file.GetBasicPropertiesAsync();
                remote[SafeName(file.Name)] = (file, data, props.DateCreated ?? props.DateModified ?? DateTimeOffset.MinValue);
            }

            string? LocalHash(string name) => File.Exists(Path.Combine(local, name)) ? Hash(File.ReadAllBytes(Path.Combine(local, name))) : null;

            // 1) Da pasta para o app, na ordem da pasta (no Android a data de criacao local segue a ordem de gravacao).
            int conflicts = 0;
            foreach (var (name, r) in remote.OrderBy(r => r.Value.When).ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
            {
                var remoteHash = Hash(r.Data);
                folder.Files.TryGetValue(name, out var synced);
                if (remoteHash == synced)
                    continue; // a pasta nao mudou; mudanca no app (se houver) vai no passo 2
                var localHash = LocalHash(name);
                if (localHash == remoteHash) { folder.Files[name] = remoteHash; continue; }
                if (localHash is not null && localHash != synced)
                {
                    // Mudou nos dois lados: vale a pasta; a versao do app vai para os backups.
                    var backup = Path.Combine(Path.GetDirectoryName(SaveBackup.Folder) ?? _root, "external-bank-conflicts", folder.Id);
                    Directory.CreateDirectory(backup);
                    File.Copy(Path.Combine(local, name), Path.Combine(backup, $"{DateTime.Now:yyyyMMdd-HHmmss} {name}"), true);
                    conflicts++;
                }
                var path = Path.Combine(local, name);
                await File.WriteAllBytesAsync(path, r.Data);
                if (r.When != DateTimeOffset.MinValue)
                {
                    try { File.SetCreationTimeUtc(path, r.When.UtcDateTime); File.SetLastWriteTimeUtc(path, r.When.UtcDateTime); }
                    catch (Exception) { /* sem suporte no sistema de arquivos: fica a ordem de gravacao */ }
                }
                folder.Files[name] = remoteHash;
            }
            // Apagado na pasta: some do app, se o app nao mudou o arquivo.
            foreach (var name in folder.Files.Keys.Where(n => !remote.ContainsKey(n)).ToArray())
            {
                var localHash = LocalHash(name);
                if (localHash is null || localHash == folder.Files[name])
                {
                    if (localHash is not null) File.Delete(Path.Combine(local, name));
                    folder.Files.Remove(name);
                }
            }

            // 2) Do app para a pasta: arquivos novos ou alterados, e os apagados no app.
            var localFiles = new DirectoryInfo(local).GetFiles().Where(f => Extensions.Contains(f.Extension)).ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var (name, info) in localFiles)
            {
                var data = await File.ReadAllBytesAsync(info.FullName);
                var hash = Hash(data);
                if (folder.Files.TryGetValue(name, out var synced) && synced == hash)
                    continue;
                var target = remote.TryGetValue(name, out var r) ? r.File : await remoteFolder.CreateFileAsync(name);
                if (target is null)
                    throw new IOException($"não foi possível criar {name} na pasta {folder.Name}");
                await MobileDocuments.WriteVerifiedAsync(target, data);
                folder.Files[name] = hash;
            }
            foreach (var name in folder.Files.Keys.Where(n => !localFiles.ContainsKey(n)).ToArray())
            {
                if (remote.TryGetValue(name, out var r))
                    await r.File.DeleteAsync();
                folder.Files.Remove(name);
            }
            Persist();
            if (conflicts > 0)
                _status($"{conflicts} arquivo(s) da pasta {folder.Name} mudaram no app e fora dele; ficou a versão da pasta e a do app foi para os backups.");
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            _status($"Não foi possível sincronizar a pasta {folder.Name}: {ex.Message}. As alterações continuam no app e vão na próxima sincronização.");
        }
        finally
        {
            _lock.Release();
        }
    }
}
