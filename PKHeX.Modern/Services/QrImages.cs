using PKHeX.Core;
using QRCoder;

namespace PKHeX.Modern.Services;

public static class QrImages
{
    public static string Message(PK7 pk, int box = 0, int slot = 0, int copies = 1)
        => QRMessageUtil.GetMessage(pk.Clone(), box, slot, copies);
    public static byte[] Png(string message)
    {
        using var data = QRCodeGenerator.GenerateQrCode(message, QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        return code.GetGraphic(4);
    }
}
