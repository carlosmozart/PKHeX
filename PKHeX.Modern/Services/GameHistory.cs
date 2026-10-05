using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Historico de diferencas: conserva os bits de outras edicoes do save.</summary>
public sealed class GameHistory
{
    private delegate Span<byte> SpanGetter();
    private sealed record Region(SpanGetter Get);
    private sealed record Patch(Region Region, int Offset, byte[] Before, byte[] After)
    {
        public void Restore(bool redo)
        {
            var span = Region.Get(); var target = redo ? After : Before;
            for (int i = 0; i < target.Length; i++)
            {
                byte mask = (byte)(Before[i] ^ After[i]);
                span[Offset + i] = (byte)((span[Offset + i] & ~mask) | (target[i] & mask));
            }
        }
    }
    private sealed record TypePatch(SCBlock Block, SCTypeCode Before, SCTypeCode After);
    private sealed record Step(string Description, List<Patch> Patches, List<TypePatch> Types)
    {
        public long Bytes => Patches.Sum(p => (long)p.Before.Length * 2 + 64) + Types.Count * 32L;
        public void Restore(bool redo) { foreach (var p in Patches) p.Restore(redo); foreach (var p in Types) p.Block.ChangeBooleanType(redo ? p.After : p.Before); }
    }
    private readonly SaveFile _sav;
    private readonly List<Region> _regions = [];
    private readonly IReadOnlyList<SCBlock> _blocks;
    private readonly List<Step> _undo = [];
    private readonly Stack<Step> _redo = new();
    private bool _editing;
    public const int Limit = 30;
    public const long MemoryLimit = 64 * 1024 * 1024;
    public int Count => _undo.Count;
    public long StoredBytes => _undo.Concat(_redo).Sum(s => s.Bytes);
    public bool CanUndo => Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => CanUndo ? _undo[^1].Description : null;
    public string? RedoDescription => CanRedo ? _redo.Peek().Description : null;
    public GameHistory(SaveFile sav)
    {
        _sav = sav; _blocks = sav is ISCBlockArray sc ? sc.AllBlocks : [];
        if (_blocks.Count > 0) { foreach (var block in _blocks) _regions.Add(new(() => block.Data)); }
        else
        {
            _regions.Add(new(() => sav.Data));
            if (sav is SAV3 s3) { _regions.Add(new(() => s3.Small)); _regions.Add(new(() => s3.Large)); _regions.Add(new(() => s3.Storage)); }
            if (sav is SAV4 s4) _regions.Add(new(() => s4.General));
        }
    }
    public bool Execute(string description, Action action)
    {
        if (_editing) { action(); return false; }
        var before = _regions.Select(r => r.Get().ToArray()).ToArray();
        var types = _blocks.Select(b => b.Type).ToArray();
        var step = new Step(description, [], []); _editing = true;
        try { action(); }
        catch
        {
            for (int i = 0; i < before.Length; i++) before[i].CopyTo(_regions[i].Get());
            for (int i = 0; i < types.Length; i++) if (_blocks[i].Type != types[i]) _blocks[i].ChangeBooleanType(types[i]);
            throw;
        }
        finally { _editing = false; }
        for (int r = 0; r < _regions.Count; r++)
        {
            var after = _regions[r].Get(); var old = before[r];
            for (int i = 0; i < old.Length;)
            {
                if (old[i] == after[i]) { i++; continue; }
                int start = i++; while (i < old.Length && old[i] != after[i]) i++;
                step.Patches.Add(new(_regions[r], start, old[start..i], after[start..i].ToArray()));
            }
        }
        for (int i = 0; i < types.Length; i++) if (types[i] != _blocks[i].Type) step.Types.Add(new(_blocks[i], types[i], _blocks[i].Type));
        if (step.Patches.Count + step.Types.Count == 0) return false;
        _redo.Clear(); _undo.Add(step);
        while (_undo.Count > Limit || StoredBytes > MemoryLimit && _undo.Count > 1) _undo.RemoveAt(0);
        _sav.State.Edited = true; return true;
    }
    public string? Undo()
    {
        if (!CanUndo) return null; var step = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); step.Restore(false); _redo.Push(step); _sav.State.Edited = true; return step.Description;
    }
    public string? Redo()
    {
        if (!CanRedo) return null; var step = _redo.Pop(); step.Restore(true); _undo.Add(step); _sav.State.Edited = true; return step.Description;
    }
}
