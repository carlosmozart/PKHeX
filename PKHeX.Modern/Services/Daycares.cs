using System;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

public static class Daycares
{
    public static IDaycareStorage[] All(SaveFile sav) => sav switch
    {
        IDaycareMulti multi => [.. Enumerable.Range(0, multi.DaycareCount).Select(i => multi[i])],
        IDaycareStorage storage => [storage],
        SAV8SWSH sw => [new SwordShieldDaycare(sw.Daycare, 0, sw.SIZE_STORED), new SwordShieldDaycare(sw.Daycare, 1, sw.SIZE_STORED)],
        _ => [],
    };
    public static PKM Read(SaveFile sav, IDaycareStorage storage, int slot) => storage.IsDaycareOccupied(slot)
        ? sav.GetStoredSlot(storage.GetDaycareSlot(slot).Span) : sav.BlankPKM;
    public static void Write(SaveFile sav, IDaycareStorage storage, int slot, PKM pk)
    {
        var data = storage.GetDaycareSlot(slot).Span;
        sav.SetSlotFormatStored(pk.Clone(), data, EntityImportSettings.None);
        storage.SetDaycareOccupied(slot, pk.Species > 0);
        if (pk.Species == 0) data.Clear();
    }
    public sealed record SlotState(byte[] Bytes, bool Occupied, uint? Experience);
    public static SlotState[][] Capture(SaveFile sav) => [.. All(sav).Select(d => Enumerable.Range(0, d.DaycareSlotCount)
        .Select(i => new SlotState(d.GetDaycareSlot(i).ToArray(), d.IsDaycareOccupied(i), d is IDaycareExperience exp ? exp.GetDaycareEXP(i) : null)).ToArray())];
    public static void Restore(SaveFile sav, SlotState[][] states)
    {
        var all = All(sav);
        for (int d = 0; d < states.Length; d++)
            for (int i = 0; i < states[d].Length; i++)
            {
                states[d][i].Bytes.CopyTo(all[d].GetDaycareSlot(i).Span);
                all[d].SetDaycareOccupied(i, states[d][i].Occupied);
                if (all[d] is IDaycareExperience exp && states[d][i].Experience is { } value) exp.SetDaycareEXP(i, value);
            }
    }

    // SW/SH expoe o bloco, mas nao implementa as interfaces da creche.
    private sealed class SwordShieldDaycare(Daycare8 block, int area, int size) : IDaycareStorage, IDaycareRandomState<ulong>
    {
        public int DaycareSlotCount => 2;
        public Memory<byte> GetDaycareSlot(int index) => block[area * 2 + index][..size];
        public bool IsDaycareOccupied(int index) => area == 0 ? block.GetDaycare1SlotOccupied(index) : block.GetDaycare2SlotOccupied(index);
        public void SetDaycareOccupied(int index, bool occupied) => block.Data[area == 0 ? Daycare8.GetDaycare1StructOffset(index) : Daycare8.GetDaycare2StructOffset(index)] = occupied ? (byte)1 : (byte)0;
        public ulong Seed { get => block.GetDaycareSeed(area); set => block.SetDaycareSeed(area, value); }
    }
}
