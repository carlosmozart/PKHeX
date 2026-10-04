using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace PKHeX.Modern.Services;

/// <summary>Documento SAF e copia privada persistente. URIs content:// nunca viram caminhos locais.</summary>
public sealed class MobileDocuments
{
    public const int MaxBytes = 64 * 1024 * 1024;
    /// <param name="Relative">Saves da pasta de saves: caminho dentro da pasta escolhida (o acesso vem da pasta, nao do arquivo).</param>
    /// <param name="Stamp">Tamanho e data do original na ultima leitura (pula a releitura quando nada mudou).</param>
    public sealed record Document(string Id, string Name, string Path, string? Bookmark, string Hash, string? Relative = null, string? Stamp = null);

    /// <summary>Acha o arquivo original de um save da pasta de saves depois de reabrir o app (definido pelo MobileSaveFolder).</summary>
    public Func<Document, IStorageProvider, Task<IStorageFile?>>? ResolveRelative { get; set; }
    private readonly string _root;
    private readonly Dictionary<string, IStorageFile> _handles = new();
    private readonly Dictionary<string, Document> _documents;
    public MobileDocuments(string root)
    {
        _root = root;
        Directory.CreateDirectory(Path.Combine(root, "documents"));
        Directory.CreateDirectory(Path.Combine(root, "document-backups"));
        var index = Path.Combine(root, "documents.json");
        _documents = File.Exists(index)
            ? JsonSerializer.Deserialize<Dictionary<string, Document>>(File.ReadAllText(index)) ?? new() : new();
    }
    public IReadOnlyList<Document> Recent => _documents.Values.Where(d => File.Exists(d.Path)).Reverse().ToArray();
    public Document? Find(string path) => _documents.Values.FirstOrDefault(d => d.Path == ZipSaves.FileOf(path));
    public static async Task<byte[]> ReadBoundedAsync(Stream input)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            if (output.Length + read > MaxBytes) throw new IOException("Arquivo maior que 64 MiB.");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return output.ToArray();
    }
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    public string PathFor(IStorageFile file)
    {
        var id = Hash(System.Text.Encoding.UTF8.GetBytes(file.Path.ToString()));
        var name = Path.GetFileName(file.Name.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "save.sav";
        return Path.Combine(_root, "documents", id, name);
    }
    public async Task<Document> ImportAsync(IStorageFile file)
    {
        await using var input = await file.OpenReadAsync();
        var bytes = await ReadBoundedAsync(input);
        var path = PathFor(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var validation = path + ".import";
        await File.WriteAllBytesAsync(validation, bytes);
        try
        {
            bool zip = Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);
            if (zip)
            {
                using var archive = ZipFile.OpenRead(validation);
                if (archive.Entries.Count > 2048 || archive.Entries.Sum(e => e.Length) > MaxBytes)
                    throw new IOException("ZIP com entradas demais ou conteúdo descompactado maior que 64 MiB.");
            }
            if (zip ? !ZipSaves.ReadAll(validation).Any() : CoreAdapter.LoadSave(validation) is null)
                throw new IOException("Este arquivo não contém um save reconhecido.");
        }
        finally { File.Delete(validation); }
        if (File.Exists(path)) SaveBackup.BeforeOverwrite(path);
        await File.WriteAllBytesAsync(path, bytes);
        string? bookmark = null;
        try { bookmark = await file.SaveBookmarkAsync(); } catch (NotSupportedException) { }
        var id = Path.GetFileName(Path.GetDirectoryName(path))!;
        var doc = new Document(id, file.Name, path, bookmark, Hash(bytes));
        _documents[id] = doc;
        _handles[id] = file;
        Persist();
        return doc;
    }
    private void Persist()
    {
        var path = Path.Combine(_root, "documents.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(_documents));
        File.Move(path + ".tmp", path, true);
    }
    public async Task SaveAsync(Document doc, byte[] bytes, IStorageProvider provider)
    {
        if (bytes.Length > MaxBytes) throw new IOException("Arquivo maior que 64 MiB.");
        if (!_handles.TryGetValue(doc.Id, out var file))
        {
            file = doc.Bookmark is not null ? await provider.OpenFileBookmarkAsync(doc.Bookmark)
                : doc.Relative is not null && ResolveRelative is { } resolve ? await resolve(doc, provider) : null;
            if (file is null) throw new IOException("O Android não concedeu acesso ao arquivo original. Use Salvar como.");
            _handles[doc.Id] = file;
        }
        byte[] original;
        await using (var input = await file.OpenReadAsync()) original = await ReadBoundedAsync(input);
        if (Hash(original) != doc.Hash)
            throw new IOException("O arquivo original mudou em outro app. Abra-o novamente ou use Salvar como para preservar as duas versões.");
        var folder = Path.Combine(_root, "document-backups", doc.Id);
        Directory.CreateDirectory(folder);
        var backup = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Path.GetFileName(doc.Path)}");
        await File.WriteAllBytesAsync(backup, original);
        try { await WriteVerifiedAsync(file, bytes); }
        catch
        {
            // Provedores SAF nao oferecem troca atomica. O backup completo existe antes de qualquer escrita.
            try { await WriteVerifiedAsync(file, original); } catch { }
            throw;
        }
        _documents[doc.Id] = doc with { Hash = Hash(bytes) };
        Persist();
        foreach (var old in new DirectoryInfo(folder).GetFiles().OrderByDescending(f => f.Name).Skip(20)) old.Delete();
    }
    public async Task ExportCopyAsync(IStorageFile file, byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new IOException("Arquivo maior que 64 MiB.");
        // Salvar como tambem pode selecionar um arquivo existente: preservar seu conteudo antes.
        byte[] previous;
        await using (var input = await file.OpenReadAsync()) previous = await ReadBoundedAsync(input);
        var folder = Path.Combine(_root, "document-backups", Path.GetFileName(Path.GetDirectoryName(PathFor(file)))!);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Path.GetFileName(file.Name)}"), previous);
        try { await WriteVerifiedAsync(file, bytes); }
        catch
        {
            try { await WriteVerifiedAsync(file, previous); } catch { }
            throw;
        }
        foreach (var old in new DirectoryInfo(folder).GetFiles().OrderByDescending(f => f.Name).Skip(20)) old.Delete();
    }
    public static async Task WriteVerifiedAsync(IStorageFile file, byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new IOException("Arquivo maior que 64 MiB.");
        await using (var output = await file.OpenWriteAsync())
        {
            if (output.CanSeek) { output.Position = 0; output.SetLength(0); }
            await output.WriteAsync(bytes);
            await output.FlushAsync();
        }
        await using var verify = await file.OpenReadAsync();
        if (Hash(await ReadBoundedAsync(verify)) != Hash(bytes))
            throw new IOException("A gravação não foi confirmada pelo provedor. O backup foi preservado; tente Salvar como.");
    }
    /// <summary>Saves registrados pela pasta de saves (Relative preenchido).</summary>
    public IEnumerable<Document> FolderDocuments => _documents.Values.Where(d => d.Relative is not null).ToArray();

    /// <summary>Registra (ou atualiza) um save da pasta de saves, ja copiado para <paramref name="localPath"/>.</summary>
    public Document Register(IStorageFile file, string localPath, string relative, string hash, string stamp)
    {
        var id = Hash(System.Text.Encoding.UTF8.GetBytes(file.Path.ToString()));
        var doc = new Document(id, file.Name, localPath, null, hash, relative, stamp);
        _documents[id] = doc;
        _handles[id] = file;
        Persist();
        return doc;
    }

    /// <summary>Mesmo original, sem mudanca: so guarda o arquivo aberto nesta sessao (Salvar nao precisa procurar de novo).</summary>
    public void Attach(Document doc, IStorageFile file) => _handles[doc.Id] = file;

    /// <summary>Tira da biblioteca um save que sumiu da pasta (a copia privada e apagada).</summary>
    public void Forget(Document doc)
    {
        _documents.Remove(doc.Id);
        _handles.Remove(doc.Id);
        Persist();
        try { if (File.Exists(doc.Path)) File.Delete(doc.Path); } catch (IOException) { }
    }

    public static string HashOf(byte[] data) => Hash(data);

    public IReadOnlyList<string> Backups(Document doc)
    {
        var folder = Path.Combine(_root, "document-backups", doc.Id);
        return Directory.Exists(folder) ? Directory.GetFiles(folder).OrderByDescending(p => p).ToArray() : [];
    }
}
