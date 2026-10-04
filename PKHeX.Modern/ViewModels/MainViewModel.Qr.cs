using System;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class MainViewModel
{
    private QrPreviewViewModel? _qr;
    public QrPreviewViewModel? Qr { get => _qr; private set { if (Set(ref _qr, value)) Raise(nameof(HasQr)); } }
    public bool HasQr => Qr is not null;
    public Func<byte[], Task>? SaveQrImage { get; set; }
    public void CloseQr() { var old = Qr; Qr = null; old?.Dispose(); }
    public void ShowPokemonQr(PK7 pk)
    {
        int box = Math.Max(0, _selectedSlot?.Box ?? 0), slot = _selectedSlot?.IsParty == false ? _selectedSlot.Slot : 0;
        ShowQr("QR da Gen 7", "Escaneie no QR Scanner de Sun, Moon, Ultra Sun ou Ultra Moon. O QR registra a espécie na Pokédex.", QrImages.Message(pk, box, slot));
    }
    public void ShowCardQr(DataMysteryGift gift)
    {
        if (gift.Generation is not (6 or 7)) return;
        ShowQr("QR do cartão", "QR de cartão no formato do PKHeX. Não entrega o evento pelo QR Scanner da Gen 7.", QRMessageUtil.GetMessage(gift));
    }
    private void ShowQr(string title, string note, string message)
    {
        try
        {
            CloseQr(); Qr = new QrPreviewViewModel(title, note, message, CloseQr, png => SaveQrImage?.Invoke(png) ?? Task.CompletedTask);
        }
        catch (Exception ex) { Status = $"Não foi possível gerar o QR: {ex.Message}"; }
    }
}
