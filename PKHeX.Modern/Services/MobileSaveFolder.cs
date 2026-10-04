using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace PKHeX.Modern.Services;

/// <summary>
/// Pasta de saves no Android. A pasta do seletor (content://) e lida inteira, com subpastas, e cada arquivo vira uma
/// copia privada em documents/&lt;pasta&gt;/&lt;caminho&gt;, que a pagina Saves lista como no desktop. Cada copia e um documento
/// do <see cref="MobileDocuments"/>: Salvar grava de volta no arquivo original, com a mesma conferencia e backups.
/// Atualizar rele so o que mudou (tamanho ou data); um save com alteracoes no app nao e trocado pelo da pasta.
/// </summary>
public sealed class MobileSaveFolder
{
    private sealed record State(string Name, string Bookmark);

    /// <summary>Arquivos menores que isso nao sao saves (o menor, Gen 1/2, tem 32 KiB); zips sempre entram.</summary>
    private const long MinSaveSize = 8 * 1024;

    private readonly string _documentsRoot;
    private readonly string _stateFile;
    private readonly MobileDocuments _documents;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private State? _state;
    private IStorageFolder? _handle;

    public MobileSaveFolder(string root, MobileDocuments documents)
    {
        _documentsRoot = Path.Combine(root, "documents");
        _stateFile = Path.Combine(root, "save-folder.json");
        _documents = documents;
        _state = File.Exists(_stateFile) ? JsonSerializer.Deserialize<State>(File.ReadAllText(_stateFile)) : null;
        documents.ResolveRelative = ResolveAsync;
    }

    /// <summary>Save aberto numa aba: nao e trocado pela versao da pasta (Salvar avisa se o original mudou).</summary>
    public Func<string, bool>? IsOpen { get; set; }

    /// <summary>Nome da pasta escolhida (nulo se nenhuma).</summary>
    public string? FolderName => _state?.Name;

    private string LocalRoot => Path.Combine(_documentsRoot, "pasta");

    /// <summary>Escolhe a pasta de saves no seletor e le os saves dela. Retorna quantos saves vieram (ou nulo se cancelou).</summary>
    public async Task<int?> ChooseAsync(IStorageProvider provider)
    {
        var picked = (await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta de saves", AllowMultiple = false })).FirstOrDefault();
        if (picked is null)
            return null;
        var bookmark = await picked.SaveBookmarkAsync() ?? throw new IOException("O Android não deixou guardar o acesso a esta pasta.");
        // Outra pasta: os saves da anterior saem da lista (os que tinham alteracoes no app continuam la).
        if (_state is not null && _state.Bookmark != bookmark)
            foreach (var doc in _documents.FolderDocuments)
                if (!HasLocalChanges(doc))
                    _documents.Forget(doc);
        _state = new State(picked.Name, bookmark);
        _handle = picked;
        File.WriteAllText(_stateFile, JsonSerializer.Serialize(_state));
        return await SyncAsync(provider);
    }

    private static bool HasLocalChanges(MobileDocuments.Document doc)
        => File.Exists(doc.Path) && MobileDocuments.HashOf(File.ReadAllBytes(doc.Path)) != doc.Hash;

    private async Task<IStorageFolder?> OpenAsync(IStorageProvider provider)
    {
        if (_handle is not null || _state is null)
            return _handle;
        return _handle = await provider.OpenFolderBookmarkAsync(_state.Bookmark);
    }

    /// <summary>Resumo da ultima leitura, para a barra de status (quantos arquivos, saves, ignorados e o primeiro erro).</summary>
    public string LastSummary { get; private set; } = "";

    /// <summary>
    /// Rele a pasta: traz saves novos e mudados, tira os que sumiram. Retorna quantos saves a pasta tem
    /// (nulo sem pasta escolhida). Um arquivo ou subpasta com erro e pulado (contado no resumo e gravado no crash.log).
    /// </summary>
    public async Task<int?> SyncAsync(IStorageProvider provider)
    {
        if (_state is null)
            return null;
        await _lock.WaitAsync();
        try
        {
            var folder = await OpenAsync(provider);
            if (folder is null)
                throw new IOException($"Sem acesso à pasta {_state.Name}. Escolha a pasta de novo.");
            var known = _documents.FolderDocuments.ToDictionary(d => d.Relative!, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var errors = new List<string>();
            var files = await ListAsync(folder, "", errors);
            int count = 0, skipped = 0;
            foreach (var (file, relative) in files)
            {
                try
                {
                    if (await SyncFileAsync(file, relative, known.GetValueOrDefault(relative)))
                    {
                        seen.Add(relative);
                        count++;
                    }
                    else skipped++;
                }
                catch (Exception ex)
                {
                    // Na duvida, o save continua na lista (nao e apagado por um erro de leitura).
                    seen.Add(relative);
                    CrashLog.Write(ex);
                    errors.Add($"{relative}: {ex.Message}");
                }
            }
            foreach (var (relative, doc) in known)
                if (!seen.Contains(relative) && !HasLocalChanges(doc))
                    _documents.Forget(doc);
            LastSummary = $"Pasta {_state.Name}: {files.Count} arquivo(s), {count} lido(s) como possível save, {skipped} ignorado(s) pelo tamanho"
                + (errors.Count > 0 ? $", {errors.Count} com erro (ex.: {errors[0]})" : "") + ".";
            return count;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Copia um arquivo da pasta (se mudou). Falso quando o tamanho mostra que nao e save.</summary>
    private async Task<bool> SyncFileAsync(IStorageFile file, string relative, MobileDocuments.Document? doc)
    {
        var props = await file.GetBasicPropertiesAsync();
        long size = (long)(props.Size ?? 0);
        bool zip = relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (size > MobileDocuments.MaxBytes || (!zip && size > 0 && size < MinSaveSize))
            return false;
        var stamp = $"{size}:{props.DateModified?.UtcTicks}";
        // Sem data do provedor, tamanho e data nao bastam: rele e compara o conteudo.
        if (doc is not null && props.DateModified is not null && doc.Stamp == stamp && File.Exists(doc.Path))
        {
            _documents.Attach(doc, file);
            return true;
        }
        if (doc is not null && (HasLocalChanges(doc) || IsOpen?.Invoke(doc.Path) == true))
        {
            _documents.Attach(doc, file); // alteracoes no app ainda nao salvas: Salvar avisa se o original mudou
            return true;
        }
        byte[] data;
        await using (var input = await file.OpenReadAsync())
            data = await MobileDocuments.ReadBoundedAsync(input);
        if (!zip && data.Length < MinSaveSize)
            return false;
        if (doc is not null && File.Exists(doc.Path) && MobileDocuments.HashOf(data) == doc.Hash)
        {
            _documents.Register(file, doc.Path, relative, doc.Hash, stamp); // mesmo conteudo (ex.: gravado pelo Salvar): so guarda a data nova
            return true;
        }
        var local = Path.Combine(LocalRoot, Path.Combine(relative.Split('/').Select(SafeName).ToArray()));
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        await File.WriteAllBytesAsync(local, data);
        if (props.DateModified is { } modified)
        {
            try { File.SetLastWriteTimeUtc(local, modified.UtcDateTime); } catch (Exception) { }
        }
        _documents.Register(file, local, relative, MobileDocuments.HashOf(data), stamp);
        return true;
    }

    /// <summary>Todos os arquivos da pasta e das subpastas; uma subpasta que o Android nao deixa listar e pulada.</summary>
    private static async Task<List<(IStorageFile File, string Relative)>> ListAsync(IStorageFolder folder, string prefix, List<string> errors)
    {
        var result = new List<(IStorageFile, string)>();
        var subfolders = new List<(IStorageFolder, string)>();
        try
        {
            await foreach (var item in folder.GetItemsAsync())
            {
                var name = prefix.Length == 0 ? item.Name : prefix + "/" + item.Name;
                if (item is IStorageFile file)
                    result.Add((file, name));
                else if (item is IStorageFolder sub)
                    subfolders.Add((sub, name));
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            errors.Add($"{(prefix.Length == 0 ? "pasta escolhida" : prefix)}: {ex.Message}");
        }
        foreach (var (sub, name) in subfolders)
            result.AddRange(await ListAsync(sub, name, errors));
        return result;
    }

    /// <summary>Depois de reabrir o app: acha o original pelo caminho dentro da pasta escolhida.</summary>
    private async Task<IStorageFile?> ResolveAsync(MobileDocuments.Document doc, IStorageProvider provider)
    {
        if (await OpenAsync(provider) is not { } folder || doc.Relative is null)
            return null;
        var parts = doc.Relative.Split('/');
        IStorageFolder current = folder;
        foreach (var part in parts[..^1])
        {
            if (await current.GetFolderAsync(part) is not { } next)
                return null;
            current = next;
        }
        return await current.GetFileAsync(parts[^1]);
    }

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) || name is "." or ".." ? "_" : name;
    }
}
