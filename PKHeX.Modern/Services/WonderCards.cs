using System;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

public static class WonderCards
{
    public static bool Supported(SaveFile sav) => sav is IMysteryGiftStorageProvider && sav.Generation is >= 4 and <= 7;
    public static DataMysteryGift[] Read(SaveFile sav)
    {
        if (!Supported(sav)) return [];
        var store = ((IMysteryGiftStorageProvider)sav).MysteryGiftStorage;
        try
        {
            var cards = Enumerable.Range(0, store.GiftCountMax).Select(store.GetMysteryGift).ToList();
            if (sav is SAV4HGSS hg) cards.Add(hg.LockCapsuleSlot.Clone());
            return [.. cards];
        }
        finally { if (store is MysteryBlock5 five) five.EndAccess(); }
    }

    private static void Write(SaveFile sav, DataMysteryGift[] cards)
    {
        var store = ((IMysteryGiftStorageProvider)sav).MysteryGiftStorage;
        try
        {
            if (store is MysteryBlock4 four)
            {
                MysteryBlock4.UpdateSlotPGT(cards, sav is SAV4HGSS);
                four.IsDeliveryManActive = cards.Any(c => !c.IsEmpty);
                if (sav is SAV4HGSS hg) hg.LockCapsuleSlot = (PCD)cards[^1];
            }
            for (int i = 0; i < store.GiftCountMax; i++) store.SetMysteryGift(i, cards[i]);
        }
        finally { if (store is MysteryBlock5 five) five.EndAccess(); }
    }

    public static int Add(SaveFile sav, DataMysteryGift source, out string reason)
    {
        reason = "";
        if (!Supported(sav) || source.IsEmpty || !source.IsCardCompatible(sav, out reason))
        { if (reason.Length == 0) reason = "Este jogo não aceita esse cartão."; return -1; }
        var copy = sav.Clone(); var cards = Read(copy); var gift = source.Clone();
        if (sav is SAV7b && gift is not WR7)
        { reason = "Let's Go guarda registros WR7, não cartões pendentes de entrega."; return -1; }
        int slot;
        if (gift is PCD { IsLockCapsule: true })
        {
            if (copy is not SAV4HGSS || !cards[^1].IsEmpty)
            { reason = "O espaço especial da Lock Capsule não está disponível."; return -1; }
            slot = cards.Length - 1;
        }
        else
            slot = Array.FindIndex(cards, c => c.IsEmpty && c.GetType() == gift.GetType());
        if (slot < 0) { reason = "Não há espaço livre para este tipo de cartão."; return -1; }
        gift.GiftUsed = false;
        cards[slot] = gift;
        if (gift is PCD { CanConvertToPGT: true } pcd)
        {
            int pending = Array.FindIndex(cards, c => c is PGT && c.IsEmpty);
            if (pending < 0) { reason = "Não há espaço livre para o presente PGT do cartão."; return -1; }
            cards[pending] = pcd.Gift.Clone();
        }
        Write(copy, cards);
        SetReceivedFlag(copy, gift.CardID, true);
        sav.CopyChangesFrom(copy); return slot;
    }

    private static void SetReceivedFlag(SaveFile sav, int id, bool received)
    {
        var store = ((IMysteryGiftStorageProvider)sav).MysteryGiftStorage;
        try
        {
            if (store is IMysteryGiftFlags flags && id > 0 && id < flags.MysteryGiftReceivedFlagMax)
                flags.SetMysteryGiftReceivedFlag(id, received);
        }
        finally { if (store is MysteryBlock5 five) five.EndAccess(); }
    }

    public static void Delete(SaveFile sav, int slot)
    {
        var copy = sav.Clone(); var cards = Read(copy);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)slot, (uint)cards.Length);
        var deleted = cards[slot];
        // O PGT associado tambem deve sair para nao deixar um presente sem cartao.
        if (deleted is PCD pcd)
            foreach (var g in cards.OfType<PGT>()) if (pcd.GiftEquals(g)) g.Clear();
        deleted.Clear();
        for (int i = slot; i + 1 < cards.Length && cards[i + 1].GetType() == deleted.GetType() && !cards[i + 1].IsEmpty; i++)
            (cards[i], cards[i + 1]) = (cards[i + 1], cards[i]);
        Write(copy, cards); sav.CopyChangesFrom(copy);
    }

    public static void MarkUnused(SaveFile sav, int slot)
    {
        var copy = sav.Clone(); var cards = Read(copy); var gift = cards[slot];
        if (gift is WR7 || gift.IsEmpty) return;
        gift.GiftUsed = false;
        if (gift is PCD pcd)
            foreach (var g in cards.OfType<PGT>()) if (pcd.GiftEquals(g)) g.GiftUsed = false;
        Write(copy, cards); SetReceivedFlag(copy, gift.CardID, false); sav.CopyChangesFrom(copy);
    }
}
