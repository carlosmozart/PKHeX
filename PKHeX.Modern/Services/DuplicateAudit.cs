using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PKHeX.Modern.Services;

public sealed record DuplicateGroup(string Kind, string Title, IReadOnlyList<DbEntry> Entries, IReadOnlyList<DuplicateGroup>? Children = null)
{
    public bool IsBackupPair => Children is not null;
    public int MatchedCount => Children?.Sum(g => g.Entries.GroupBy(e => e.Source.Id).Min(s => s.Count())) ?? 0;
    public bool CanCompare => Kind == "Anexado" && Entries.Skip(1).Any(e => !StoredPokemon.Equal(Entries[0].Pkm, e.Pkm));
}

public sealed record DuplicateAuditResult(IReadOnlyList<DuplicateGroup> Groups, IReadOnlyDictionary<string, string> Labels);

public static class DuplicateAudit
{
    public static DuplicateAuditResult Build(IReadOnlyList<DbEntry> entries, CancellationToken token = default)
    {
        var identical = new List<DuplicateGroup>();
        var labels = new Dictionary<string, string>();
        foreach (var bucket in entries.GroupBy(e => StoredPokemon.Index(e.Pkm)))
        {
            token.ThrowIfCancellationRequested();
            // SHA is only an index. Partition the bucket with final byte comparisons.
            var partitions = new List<List<DbEntry>>();
            foreach (var entry in bucket)
            {
                var match = partitions.FirstOrDefault(g => StoredPokemon.Equal(g[0].Pkm, entry.Pkm));
                if (match is null) partitions.Add([entry]); else match.Add(entry);
            }
            foreach (var group in partitions.Where(g => g.Count > 1))
            {
                identical.Add(new("Cópias idênticas", group[0].Species, group));
                foreach (var e in group) labels[e.LocationId] = string.Format(Loc.T("Cópia idêntica em {0}"), string.Join("; ", group.Where(other => other != e).Select(other => other.Source.Name + " · " + other.Where)));
            }
        }
        var pairs = new Dictionary<string, (string Title, List<DuplicateGroup> Groups)>();
        var pairedLocations = new HashSet<string>();
        foreach (var group in identical)
        {
            var sources = group.Entries.Where(e => !e.Source.IsBank).Select(e => e.Source).DistinctBy(s => s.Id).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
            for (int a = 0; a < sources.Length; a++)
                for (int b = a + 1; b < sources.Length; b++)
                {
                    if (!ZipSaves.IsZipPath(sources[a].Id, out _, out _) && !ZipSaves.IsZipPath(sources[b].Id, out _, out _)) continue;
                    string key = sources[a].Id + "\n" + sources[b].Id;
                    if (!pairs.TryGetValue(key, out var pair)) pair = (ZipSaves.DisplayName(sources[a].Id) + " ↔ " + ZipSaves.DisplayName(sources[b].Id), []);
                    var members = group.Entries.Where(e => e.Source.Id == sources[a].Id || e.Source.Id == sources[b].Id).ToArray();
                    pair.Groups.Add(group with { Entries = members });
                    foreach (var member in members) pairedLocations.Add(member.LocationId);
                    pairs[key] = pair;
                }
        }
        var groups = pairs.Values.Select(p => new DuplicateGroup("Backups", p.Title, p.Groups.SelectMany(g => g.Entries).DistinctBy(e => e.LocationId).ToArray(), p.Groups)).ToList();
        groups.AddRange(identical.Select(g => g.Entries.Any(e => pairedLocations.Contains(e.LocationId))
            ? g with { Entries = g.Entries.Where(e => !ZipSaves.IsZipPath(e.Source.Id, out _, out _)).ToArray() } : g).Where(g => g.Entries.Count > 1));

        // IdOf is only used after a persisted Bank link establishes the relationship.
        foreach (var link in BankLinks.All)
        {
            token.ThrowIfCancellationRequested();
            var banks = entries.Where(e => e.Source.IsBank && BankLinks.IdOf(e.Pkm) == link.Id).ToArray();
            var saves = entries.Where(e => !e.Source.IsBank && StoredPokemon.SameSource(e.Source.Id, link.SavePath) && BankLinks.IdOf(e.Pkm) == link.Id).ToArray();
            var members = banks.Concat(saves).ToList();
            if (banks.Length > 0)
                foreach (var variant in BankLinks.GetVariants(banks[0].Pkm))
                    members.Add(new DbEntry(new DbSource("variant:" + variant.File, Loc.T("Variante do Bank"), variant.Pk.Version, false, banks[0].Source.Bank), variant.Pk, Path.GetFileName(variant.File), -2, -1) { EntityFile = variant.File });
            foreach (var member in members) labels[member.LocationId] = Loc.T("Anexado");
            if (members.Count > 1) groups.Add(new("Anexado", link.Name, members));
        }
        return new(groups, labels);
    }
}
