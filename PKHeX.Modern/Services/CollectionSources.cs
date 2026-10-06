using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>One reader per page. Captures open saves on the caller/UI thread, reads only copies on the worker.</summary>
public sealed class CollectionSources
{
    private readonly Dictionary<string, (DateTime Write, SaveFile Sav)> _cache = new(StringComparer.Ordinal);
    private int _revision;
    public IReadOnlyList<string> Errors { get; private set; } = [];
    public void Invalidate() => Interlocked.Increment(ref _revision);
    private int _cacheRevision = -1;
    public async Task<IReadOnlyList<DbEntry>> ReadAsync(IReadOnlyList<(string Path, SaveFile Sav)> open, string? folder,
        bool bank, CancellationToken token, Action<int>? progress = null)
    {
        var revision = _revision;
        var snapshots = open.Select(e => (e.Path, Sav: e.Sav.Clone())).ToArray();
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (_cacheRevision != revision) { _cache.Clear(); _cacheRevision = revision; }
            var errors = new List<string>();
            var entries = PokemonDatabase.Build(snapshots, folder, _cache, bank, readOnly: true, sourceProgress: progress, cancellationToken: token, readError: errors.Add);
            token.ThrowIfCancellationRequested();
            if (revision != _revision) throw new OperationCanceledException(token);
            Errors = errors;
            return (IReadOnlyList<DbEntry>)entries;
        }, token);
    }
}

public static class StoredPokemon
{
    public static byte[] Bytes(PKM pk) => pk.Data[..pk.SIZE_STORED].ToArray();
    public static bool Equal(PKM a, PKM b) => a.GetType() == b.GetType() && a.Data[..a.SIZE_STORED].SequenceEqual(b.Data[..b.SIZE_STORED]);
    public static string Index(PKM pk) => pk.GetType().FullName + ":" + Convert.ToHexString(SHA256.HashData(pk.Data[..pk.SIZE_STORED]));
    public static bool SameSource(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
    public static string Normalize(string path)
    {
        string FileKey(string file) => OperatingSystem.IsWindows() ? Path.GetFullPath(file).ToUpperInvariant() : Path.GetFullPath(file);
        return ZipSaves.IsZipPath(path, out var zip, out var entry) ? ZipSaves.Combine(FileKey(zip), entry) : FileKey(path);
    }
}
