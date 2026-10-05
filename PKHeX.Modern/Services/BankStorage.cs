using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>
/// Uma caixa do bank: pasta "NN Nome" dentro da pasta do banco. Numa pasta externa (<see cref="ExternalIndex"/> &gt;= 0),
/// <see cref="Folder"/> e a propria pasta e a caixa e o grupo de 30 arquivos de numero <see cref="ExternalIndex"/>.
/// </summary>
public sealed record BankBox(string Folder, string Name, int ExternalIndex = -1)
{
    public bool IsExternal => ExternalIndex >= 0;
}

/// <summary>
/// Bank local: armazenamento de Pokemon fora dos saves (como o Pokemon HOME, mas offline).
/// Estrutura em disco, facil de copiar/sincronizar e de abrir no PKHeX:
/// <code>
/// %APPDATA%\PKHeX.Modern\bank\
///   Principal\                 ← banco (pasta)
///     01 Caixa 1\              ← caixa (pasta "NN Nome", NN = ordem)
///       05 Pikachu.pk3         ← Pokemon no slot 5, no formato original (sem conversao)
/// </code>
/// Gravar no bank e imediato (nao depende de exportar o save).
/// </summary>
public static class BankStorage
{
    public const int SlotsPerBox = 30;
    public const string DefaultBank = "Principal";

    public static string Root { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKHeX.Modern", "bank");

    // Pastas externas: qualquer pasta com arquivos .pk* (ex.: a pasta de Pokemon do PKHeX) usada como banco.
    // Os arquivos ficam como estao; as caixas sao grupos de 30, na ordem em que os arquivos foram criados.
    // Arquivo novo entra no fim; substituir um Pokemon mantem a posicao (a data de criacao e preservada).
    /// <summary>Pastas externas registradas (guardadas nas preferencias; o MainViewModel liga esta lista).</summary>
    public static IList<string> ExternalFolders { get; set; } = [];

    /// <summary>Um arquivo de uma pasta externa foi gravado ou apagado (Android: copia a mudanca para a pasta original).</summary>
    public static event Action<string>? ExternalChanged;

    /// <summary>Prefixo que identifica um banco externo na lista de bancos.</summary>
    public const string ExternalPrefix = "📁 ";

    public static bool IsExternalBank(string bank) => bank.StartsWith(ExternalPrefix, StringComparison.Ordinal);

    /// <summary>Nome do banco externo na lista ("📁 Pasta"); o caminho e resolvido por <see cref="GetExternalPath"/>.</summary>
    public static string GetExternalBankName(string folder) => ExternalPrefix + (Path.GetFileName(folder.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : folder);

    public static string? GetExternalPath(string bank)
        => ExternalFolders.FirstOrDefault(f => GetExternalBankName(f) == bank);

    private static readonly HashSet<string> EntityExtensions = new(
        EntityFileExtension.GetExtensionsAll().Select(e => "." + e), StringComparer.OrdinalIgnoreCase);

    /// <summary>Arquivos de Pokemon da pasta externa, na ordem das caixas (criacao, depois nome).</summary>
    private static List<FileInfo> GetExternalFiles(string folder)
        => !Directory.Exists(folder) ? [] : [.. new DirectoryInfo(folder).GetFiles()
            .Where(f => EntityExtensions.Contains(f.Extension))
            .OrderBy(f => f.CreationTimeUtc).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)];

    private static FileInfo? GetExternalFile(BankBox box, int slot)
    {
        var files = GetExternalFiles(box.Folder);
        int index = (box.ExternalIndex * SlotsPerBox) + slot;
        return index < files.Count ? files[index] : null;
    }

    // Bancos
    /// <summary>Bancos existentes (cria o "Principal" com uma caixa se ainda nao houver nenhum).</summary>
    public static IReadOnlyList<string> GetBanks(bool create = true)
    {
        if (!create && !Directory.Exists(Root)) return [];
        if (create) Directory.CreateDirectory(Root);
        var banks = Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>()
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (banks.Count == 0 && create)
        {
            CreateBank(DefaultBank);
            banks.Add(DefaultBank);
        }
        return banks;
    }

    public static string? CreateBank(string name)
    {
        name = CleanName(name);
        if (name.Length == 0)
            return "Nome inválido.";
        var dir = Path.Combine(Root, name);
        if (Directory.Exists(dir))
            return "Já existe um banco com esse nome.";
        Directory.CreateDirectory(dir);
        CreateBox(name, "Caixa 1");
        return null;
    }

    public static string? RenameBank(string bank, string newName)
    {
        newName = CleanName(newName);
        if (newName.Length == 0)
            return "Nome inválido.";
        var target = Path.Combine(Root, newName);
        if (Directory.Exists(target))
            return "Já existe um banco com esse nome.";
        Directory.Move(Path.Combine(Root, bank), target);
        return null;
    }

    /// <summary>Apaga o banco inteiro, com todas as caixas e Pokemon (a interface pede confirmacao antes).</summary>
    public static void DeleteBank(string bank) => Directory.Delete(Path.Combine(Root, bank), recursive: true);

    // Caixas
    public static IReadOnlyList<BankBox> GetBoxes(string bank, bool create = true)
    {
        if (IsExternalBank(bank))
        {
            // Sempre sobra espaco no fim para soltar mais Pokemon.
            var path = GetExternalPath(bank) ?? "";
            int count = GetExternalFiles(path).Count;
            return [.. Enumerable.Range(0, (count / SlotsPerBox) + 1)
                .Select(i => new BankBox(path, $"Arquivos {(i * SlotsPerBox) + 1}–{(i + 1) * SlotsPerBox}", i))];
        }
        var dir = Path.Combine(Root, bank);
        if (!create && !Directory.Exists(dir)) return [];
        if (create) Directory.CreateDirectory(dir);
        var boxes = Directory.GetDirectories(dir)
            .Select(d => new BankBox(d, StripOrder(Path.GetFileName(d))))
            .OrderBy(b => Path.GetFileName(b.Folder), StringComparer.Ordinal)
            .ToList();
        if (boxes.Count == 0 && create)
        {
            CreateBox(bank, "Caixa 1");
            return GetBoxes(bank);
        }
        return boxes;
    }

    public static string? CreateBox(string bank, string name)
    {
        name = CleanName(name);
        if (name.Length == 0)
            return "Nome inválido.";
        var dir = Path.Combine(Root, bank);
        Directory.CreateDirectory(dir);
        var next = Directory.GetDirectories(dir).Length + 1;
        Directory.CreateDirectory(Path.Combine(dir, $"{next:00} {name}"));
        return null;
    }

    public static string? RenameBox(BankBox box, string newName)
    {
        newName = CleanName(newName);
        if (newName.Length == 0)
            return "Nome inválido.";
        var folder = Path.GetFileName(box.Folder);
        var order = folder.Length >= 2 && char.IsDigit(folder[0]) && char.IsDigit(folder[1]) ? folder[..2] : "99";
        var target = Path.Combine(Path.GetDirectoryName(box.Folder)!, $"{order} {newName}");
        if (!string.Equals(target, box.Folder, StringComparison.OrdinalIgnoreCase))
            Directory.Move(box.Folder, target);
        return null;
    }

    public static void DeleteBox(BankBox box) => Directory.Delete(box.Folder, recursive: true);

    // Slots
    /// <summary>Le os 30 slots da caixa (null = vazio). Arquivos ilegiveis sao ignorados.</summary>
    public static PKM?[] ReadBox(BankBox box)
    {
        var result = new PKM?[SlotsPerBox];
        if (box.IsExternal)
        {
            var files = GetExternalFiles(box.Folder).Skip(box.ExternalIndex * SlotsPerBox).Take(SlotsPerBox).ToList();
            for (int i = 0; i < files.Count; i++)
                result[i] = ReadEntity(files[i].FullName);
            return result;
        }
        if (!Directory.Exists(box.Folder))
            return result;
        foreach (var file in Directory.GetFiles(box.Folder))
        {
            var slot = GetSlot(Path.GetFileName(file));
            if (slot is < 0 or >= SlotsPerBox || result[slot] is not null)
                continue;
            result[slot] = ReadEntity(file);
        }
        return result;
    }

    public static PKM? ReadSlot(BankBox box, int slot)
        => box.IsExternal ? GetExternalFile(box, slot) is { } f ? ReadEntity(f.FullName) : null
            : FindFile(box, slot) is { } file ? ReadEntity(file) : null;

    /// <summary>Grava o Pokemon no slot, no formato original dele (substitui o que houver).</summary>
    public static void WriteSlot(BankBox box, int slot, PKM pk)
    {
        if (box.IsExternal)
        {
            // Pasta externa: nome no padrao do PKHeX. Substituindo, o arquivo novo herda a posicao do antigo.
            var old = GetExternalFile(box, slot);
            var path = Path.Combine(box.Folder, CoreAdapter.GetEntityFileName(pk));
            var stem = Path.GetFileNameWithoutExtension(path);
            for (int i = 2; File.Exists(path) && !string.Equals(path, old?.FullName, StringComparison.OrdinalIgnoreCase); i++)
                path = Path.Combine(box.Folder, $"{stem} ({i}){Path.GetExtension(path)}");
            var created = old?.CreationTimeUtc;
            old?.Delete();
            CoreAdapter.ExportEntity(pk, path);
            if (created is { } c)
                File.SetCreationTimeUtc(path, c);
            ExternalChanged?.Invoke(box.Folder);
            return;
        }
        Directory.CreateDirectory(box.Folder);
        DeleteSlot(box, slot);
        var name = CoreAdapter.IsEmpty(pk) ? "Pokemon" : CleanName(pk.IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[pk.Species]);
        CoreAdapter.ExportEntity(pk, Path.Combine(box.Folder, $"{slot + 1:00} {name}.{pk.Extension}"));
    }

    /// <summary>Nao ha arquivo neste slot (um arquivo ilegivel tambem ocupa o slot).</summary>
    public static bool IsSlotFree(BankBox box, int slot) => box.IsExternal ? GetExternalFile(box, slot) is null : FindFile(box, slot) is null;

    /// <summary>
    /// Apaga o Pokemon do slot. Na pasta externa os arquivos seguintes sobem uma posicao: ao apagar varios,
    /// apague do ultimo para o primeiro.
    /// </summary>
    public static void DeleteSlot(BankBox box, int slot)
    {
        if (box.IsExternal)
        {
            if (GetExternalFile(box, slot) is { } extFile)
            {
                extFile.Delete();
                ExternalChanged?.Invoke(box.Folder);
            }
            return;
        }
        if (FindFile(box, slot) is { } file)
            File.Delete(file);
    }

    /// <summary>Ordena a caixa do bank (os Pokemon ficam nos primeiros slots, sem espacos). Retorna quantos ha na caixa.</summary>
    public static int SortBox(BankBox box, CoreAdapter.BoxSortOption option)
    {
        if (box.IsExternal)
            throw new InvalidOperationException("pastas externas ficam na ordem dos arquivos; ordene num banco do app.");
        var current = ReadBox(box).OfType<PKM>().ToList();
        // Um arquivo que nao deu para ler seria apagado ao regravar a caixa: nesse caso nao mexe em nada.
        var files = Directory.Exists(box.Folder) ? Directory.GetFiles(box.Folder).Count(f => GetSlot(Path.GetFileName(f)) is >= 0 and < SlotsPerBox) : 0;
        if (files != current.Count)
            throw new InvalidOperationException("a caixa tem arquivos que não são Pokémon válidos (ou dois arquivos no mesmo slot). Confira a pasta.");
        var sorted = CoreAdapter.Sort(current, option);
        for (int i = 0; i < SlotsPerBox; i++)
            DeleteSlot(box, i);
        for (int i = 0; i < sorted.Count; i++)
            WriteSlot(box, i, sorted[i]);
        return sorted.Count;
    }

    /// <summary>Quantos Pokemon ha no banco inteiro (para o resumo).</summary>
    public static int CountBank(string bank)
        => IsExternalBank(bank) ? GetExternalFiles(GetExternalPath(bank) ?? "").Count : GetBoxes(bank).Sum(b => Directory.Exists(b.Folder) ? Directory.GetFiles(b.Folder).Count(f => GetSlot(Path.GetFileName(f)) is >= 0 and < SlotsPerBox) : 0);

    private static string? FindFile(BankBox box, int slot)
        => Directory.Exists(box.Folder) ? Directory.GetFiles(box.Folder).FirstOrDefault(f => GetSlot(Path.GetFileName(f)) == slot) : null;

    internal static PKM? ReadEntity(string file)
    {
        try
        {
            var data = File.ReadAllBytes(file);
            return EntityFormat.GetFromBytes(data, GetContextFromExtension(Path.GetExtension(file)));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Ajuda o Core a decidir o formato (alguns tamanhos de arquivo valem para mais de um jogo).</summary>
    private static EntityContext GetContextFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".pk1" => EntityContext.Gen1, ".pk2" => EntityContext.Gen2, ".pk3" => EntityContext.Gen3, ".pk4" => EntityContext.Gen4,
        ".pk5" => EntityContext.Gen5, ".pk6" => EntityContext.Gen6, ".pk7" => EntityContext.Gen7, ".pb7" => EntityContext.Gen7b,
        ".pk8" => EntityContext.Gen8, ".pa8" => EntityContext.Gen8a, ".pb8" => EntityContext.Gen8b, ".pk9" => EntityContext.Gen9,
        ".pa9" => EntityContext.Gen9a, _ => EntityContext.None,
    };

    /// <summary>"05 Pikachu.pk3" → 4 (slot zero-based); -1 se o nome nao comeca com numero.</summary>
    private static int GetSlot(string fileName)
    {
        int i = 0, value = 0;
        while (i < fileName.Length && char.IsDigit(fileName[i]))
            value = value * 10 + (fileName[i++] - '0');
        return i == 0 ? -1 : value - 1;
    }

    private static string StripOrder(string folder)
    {
        int i = 0;
        while (i < folder.Length && char.IsDigit(folder[i]))
            i++;
        return i > 0 && i < folder.Length ? folder[i..].Trim() : folder;
    }

    private static string CleanName(string name) => PathUtil.CleanFileName(name ?? "").Trim();
}
