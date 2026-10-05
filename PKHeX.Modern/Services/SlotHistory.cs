using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>
/// Desfazer/refazer de alteracoes nos slots (mover, copiar, importar, aplicar no editor).
/// Antes de cada alteracao, guarda uma copia dos slots afetados. A equipe e guardada inteira,
/// porque o Core reordena os slots dela (remover do meio puxa os seguintes).
/// </summary>
public sealed class SlotHistory(SaveFile sav, int limit = 50)
{
    /// <summary>Slot afetado. <c>Box &lt; 0</c> representa a equipe inteira.</summary>
    public readonly record struct Key(int Box, int Slot)
    {
        public static Key Party => new(-1, 0);
        public static Key Daycare => new(-2, 0);
    }

    private sealed record Snapshot(string Description, Key[] Keys, PKM[][] Data, Daycares.SlotState[][]? Daycare);

    private readonly List<Snapshot> _undo = [];
    private readonly Stack<Snapshot> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    /// <summary>Descricao de cada alteracao que pode ser desfeita, da mais antiga para a mais nova.</summary>
    public IReadOnlyList<string> Descriptions => [.. _undo.Select(u => u.Description)];
    public int Count => _undo.Count;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => CanUndo ? _undo[^1].Description : null;
    public string? RedoDescription => CanRedo ? _redo.Peek().Description : null;

    /// <summary>Chave de um slot (box &lt; 0 = equipe).</summary>
    public static Key KeyOf(int box, int slot) => box < 0 ? Key.Party : new Key(box, slot);

    /// <summary>Guarda o estado atual dos slots antes de uma alteracao. Limpa o refazer.</summary>
    public void Record(string description, params Key[] keys)
    {
        keys = [.. keys.Distinct()];
        _undo.Add(Capture(description, keys));
        if (_undo.Count > limit)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Descarta o ultimo registro (a alteracao nao aconteceu, ex.: erro ao mover).</summary>
    public void Discard()
    {
        if (CanUndo)
            _undo.RemoveAt(_undo.Count - 1);
    }

    /// <summary>Volta os slots do ultimo registro e o descarta, sem refazer (uma operacao em lote que falhou no meio).</summary>
    public void Rollback()
    {
        if (!CanUndo)
            return;
        var snap = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(snap);
    }

    /// <summary>Desfaz a ultima alteracao. Retorna a descricao ou null.</summary>
    public string? Undo()
    {
        if (!CanUndo)
            return null;
        var snap = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Push(Capture(snap.Description, snap.Keys));
        Restore(snap);
        return snap.Description;
    }

    /// <summary>Refaz a ultima alteracao desfeita. Retorna a descricao ou null.</summary>
    public string? Redo()
    {
        if (!CanRedo)
            return null;
        var snap = _redo.Pop();
        _undo.Add(Capture(snap.Description, snap.Keys));
        Restore(snap);
        return snap.Description;
    }

    private Snapshot Capture(string description, Key[] keys)
        => new(description, keys, [.. keys.Select(k => k.Box == -1
            ? sav.PartyData.Select(p => p.Clone()).ToArray()
            : k.Box < -1 ? [] : new[] { sav.GetBoxSlotAtIndex(k.Box, k.Slot).Clone() })],
            keys.Any(k => k.Box < -1) ? Daycares.Capture(sav) : null);

    private void Restore(Snapshot snap)
    {
        if (snap.Daycare is { } daycare) Daycares.Restore(sav, daycare);
        for (int i = 0; i < snap.Keys.Length; i++)
        {
            var key = snap.Keys[i];
            var data = snap.Data[i];
            if (key.Box < -1) continue;
            if (key.Box >= 0)
            {
                sav.SetBoxSlotAtIndex(data[0].Clone(), key.Box, key.Slot, EntityImportSettings.None);
                continue;
            }
            // Equipe: limpa de tras para frente e regrava na ordem (o Core ajusta o PartyCount).
            for (int s = 5; s >= data.Length; s--)
                sav.SetPartySlotAtIndex(sav.BlankPKM, s, EntityImportSettings.None);
            for (int s = 0; s < data.Length; s++)
                sav.SetPartySlotAtIndex(data[s].Clone(), s, EntityImportSettings.None);
        }
    }
}
