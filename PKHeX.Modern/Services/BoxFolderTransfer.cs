using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace PKHeX.Modern.Services;

/// <summary>Pastas do seletor podem ser content://; nenhum caminho externo e necessario.</summary>
public static class BoxFolderTransfer
{
    public static async Task<IReadOnlyList<string>> ReadAsync(IStorageFolder folder, string temporary)
    {
        Directory.CreateDirectory(temporary);
        var files = new List<string>();
        await foreach (var item in folder.GetItemsAsync())
        {
            using (item)
            {
                if (item is IStorageFolder child)
                    files.AddRange(await ReadAsync(child, Path.Combine(temporary, Guid.NewGuid().ToString("N"))));
                else if (item is IStorageFile file)
                {
                    var path = Path.Combine(temporary, files.Count.ToString("D6") + "-" + PKHeX.Core.PathUtil.CleanFileName(file.Name));
                    await using var input = await file.OpenReadAsync();
                    using var output = new MemoryStream();
                    var buffer = new byte[8192]; int length;
                    while ((length = await input.ReadAsync(buffer)) > 0)
                    {
                        if (output.Length + length > 64 * 1024 * 1024) throw new IOException("Arquivo maior que 64 MiB.");
                        await output.WriteAsync(buffer.AsMemory(0, length));
                    }
                    await File.WriteAllBytesAsync(path, output.ToArray()); files.Add(path);
                }
            }
        }
        return files;
    }

    public static async Task<int> WriteAsync(string source, IStorageFolder folder)
    {
        int count = 0;
        foreach (var path in Directory.EnumerateDirectories(source))
        {
            using var child = await folder.GetFolderAsync(Path.GetFileName(path)) ?? await folder.CreateFolderAsync(Path.GetFileName(path)) ?? throw new IOException("Não foi possível criar a subpasta.");
            count += await WriteAsync(path, child);
        }
        foreach (var path in Directory.EnumerateFiles(source))
        {
            using var existing = await folder.GetFileAsync(Path.GetFileName(path));
            if (existing is not null) continue;
            using var file = await folder.CreateFileAsync(Path.GetFileName(path)) ?? throw new IOException("Não foi possível criar o arquivo.");
            await MobileDocuments.WriteVerifiedAsync(file, await File.ReadAllBytesAsync(path)); count++;
        }
        return count;
    }
}
