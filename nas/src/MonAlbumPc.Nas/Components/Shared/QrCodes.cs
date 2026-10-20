using QRCoder;

namespace MonAlbumPc.Nas.Components.Shared;

public static class QrCodes
{
    /// <summary>The QR code of <paramref name="text"/> as a PNG <c>data:</c> URI, for an <c>img</c> tag.</summary>
    public static string DataUri(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return "data:image/png;base64," + Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(8));
    }
}
