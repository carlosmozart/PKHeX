using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace PKHeX.Modern.Sprites;

/// <summary>
/// Imagem em memoria no mesmo formato que o PKHeX usa (System.Drawing 32bppArgb): BGRA, sem pre-multiplicacao,
/// linha a linha sem sobra. As operacoes sao as do <c>PKHeX.Drawing.ImageUtil</c>, portadas byte a byte.
/// </summary>
public sealed class SpriteImage
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Pixels BGRA (4 bytes por pixel, <see cref="Width"/> x <see cref="Height"/>).</summary>
    public byte[] Pixels { get; }

    public SpriteImage(int width, int height, byte[]? pixels = null)
    {
        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[width * height * 4];
        if (Pixels.Length != width * height * 4)
            throw new ArgumentException("tamanho dos pixels nao bate com a imagem", nameof(pixels));
    }

    public SpriteImage Clone() => new(Width, Height, (byte[])Pixels.Clone());

    // ImageUtil: camadas

    /// <summary>Copia de <paramref name="baseLayer"/> com <paramref name="overLayer"/> desenhada em (x, y), no tamanho original.</summary>
    public static SpriteImage LayerImage(SpriteImage baseLayer, SpriteImage overLayer, int x, int y)
    {
        var result = baseLayer.CopyAsGdi();
        result.DrawOver(overLayer, x, y);
        return result;
    }

    public static SpriteImage LayerImage(SpriteImage baseLayer, SpriteImage overLayer, int x, int y, double transparency)
    {
        var over = overLayer.Clone();
        over.ChangeOpacity(transparency);
        return LayerImage(baseLayer, over, x, y);
    }

    /// <summary>
    /// Copia como o <c>new Bitmap(imagem)</c> do GDI+, que desenha a imagem num bitmap vazio: pixels transparentes
    /// ficam zerados e os semitransparentes passam pela pre-multiplicacao e voltam (com o arredondamento do GDI+).
    /// </summary>
    public SpriteImage CopyAsGdi()
    {
        var result = new SpriteImage(Width, Height);
        result.DrawOver(this, 0, 0);
        return result;
    }

    /// <summary>
    /// Desenha por cima (SourceOver) como o <c>Graphics.DrawImage</c> do GDI+ num bitmap 32bppArgb: a conta e feita
    /// pre-multiplicada e convertida de volta.
    /// </summary>
    public void DrawOver(SpriteImage src, int x, int y)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + src.Width), y1 = Math.Min(Height, y + src.Height);
        var d = Pixels;
        var s = src.Pixels;
        for (int py = y0; py < y1; py++)
        {
            for (int px = x0; px < x1; px++)
            {
                int si = (((py - y) * src.Width) + (px - x)) * 4;
                int sa = s[si + 3];
                int di = ((py * Width) + px) * 4;
                if (sa == 0)
                {
                    RoundTrip(d, di);
                    continue;
                }
                if (sa == 255)
                {
                    d[di] = s[si];
                    d[di + 1] = s[si + 1];
                    d[di + 2] = s[si + 2];
                    d[di + 3] = 255;
                    continue;
                }
                Blend(d, di, s, si, sa);
            }
        }
    }

    /// <summary>
    /// O GDI+ converte todo o retangulo desenhado para pre-multiplicado e volta, mesmo onde a camada de cima e
    /// transparente: pixels transparentes do destino ficam zerados e os semitransparentes perdem um pouco de precisao.
    /// </summary>
    private static void RoundTrip(byte[] d, int di)
    {
        int da = d[di + 3];
        if (da == 255)
            return;
        if (da == 0)
        {
            d[di] = d[di + 1] = d[di + 2] = 0;
            return;
        }
        int k = 0xFF0000 / da;
        for (int c = 0; c < 3; c++)
            d[di + c] = (byte)Math.Min(255, ((((d[di + c] * da) + 127) / 255) * k) >> 16);
    }

    /// <summary>
    /// Mistura de um pixel semitransparente com a mesma aritmetica do GDI+ (conferida contra o System.Drawing em 20 mil
    /// combinacoes e em imagens aleatorias): pre-multiplica arredondando, mistura arredondando e desfaz a pre-multiplicacao com a tabela de
    /// reciprocos (0xFF0000 / alpha) truncando.
    /// </summary>
    private static void Blend(byte[] d, int di, byte[] s, int si, int sa)
    {
        int da = d[di + 3];
        int inv = 255 - sa;
        int oa = ((sa * 255) + (da * inv) + 127) / 255;
        if (oa == 0)
        {
            d[di] = d[di + 1] = d[di + 2] = d[di + 3] = 0;
            return;
        }
        int k = 0xFF0000 / oa;
        for (int c = 0; c < 3; c++)
        {
            int sp = ((s[si + c] * sa) + 127) / 255;
            int dp = ((d[di + c] * da) + 127) / 255;
            int op = sp + (((dp * inv) + 127) / 255);
            d[di + c] = (byte)Math.Min(255, (op * k) >> 16);
        }
        d[di + 3] = (byte)oa;
    }

    // ImageUtil: operacoes sobre os pixels

    /// <summary>Multiplica a opacidade (truncando), como <c>ImageUtil.ChangeOpacity</c>. Ignora valores fora de (0.01, 1].</summary>
    public void ChangeOpacity(double trans)
    {
        if (trans is <= 0.01f or > 1f)
            return;
        var data = Pixels;
        for (int i = data.Length - 4; i >= 0; i -= 4)
            data[i + 3] = (byte)(data[i + 3] * trans);
    }

    /// <summary>Pinta os pixels de <paramref name="start"/> a <paramref name="end"/> (em bytes) com uma cor.</summary>
    public void WritePixels(Color c, int start, int end)
    {
        var arr = MemoryMarshal.Cast<byte, int>(Pixels.AsSpan(start, end - start));
        arr.Fill(c.ToArgb());
    }

    /// <summary>Troca os pixels quase transparentes (alpha &lt; 128) por uma cor com a opacidade dada.</summary>
    public void ChangeTransparentTo(Color c, byte trans, int start = 0, int end = -1)
    {
        if (end == -1)
            end = Pixels.Length;
        var data = Pixels.AsSpan(start, end - start);
        var arr = MemoryMarshal.Cast<byte, int>(data);
        var value = Color.FromArgb(trans, c).ToArgb();
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] < TransparencyThresholdHalf)
                arr[i >> 2] = value;
        }
    }

    public static void SetAllUsedPixelsOpaque(Span<byte> data, byte threshold = TransparencyThresholdHalf)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] >= threshold)
                data[i + 3] = 0xFF;
        }
    }

    public static void RemovePixels(Span<byte> pixels, ReadOnlySpan<byte> original, byte threshold = TransparencyThresholdHalf)
    {
        var arr = MemoryMarshal.Cast<byte, int>(pixels);
        for (int i = original.Length - 4; i >= 0; i -= 4)
        {
            if (original[i + 3] >= threshold)
                arr[i >> 2] = 0;
        }
    }

    public static void GlowEdges(Span<byte> data, byte blue, byte green, byte red, int width, int reach = 3, double amount = 0.0777)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
            data[i + PollutePixelColorIndex] = 0;
        PollutePixels(data, width, reach, amount);
        CleanPollutedPixels(data, blue, green, red);
    }

    private const int PollutePixelColorIndex = 0; // azul
    private const byte TransparencyThresholdHalf = 0x80;

    private static void PollutePixels(Span<byte> data, int width, int reach, double amount, byte threshold = TransparencyThresholdHalf)
    {
        int stride = width * 4;
        int height = data.Length / stride;
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] < threshold)
                continue;
            int x = (i % stride) / 4;
            int y = i / stride;
            int left = Math.Max(0, x - reach);
            int right = Math.Min(width - 1, x + reach);
            int top = Math.Max(0, y - reach);
            int bottom = Math.Min(height - 1, y + reach);
            for (int ix = left; ix <= right; ix++)
            {
                for (int iy = top; iy <= bottom; iy++)
                {
                    var c = 4 * (ix + (iy * width));
                    ref var b = ref data[c + PollutePixelColorIndex];
                    b += (byte)(amount * (0xFF - b));
                }
            }
        }
    }

    private static void CleanPollutedPixels(Span<byte> data, byte blue, byte green, byte red)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            var transparency = data[i + PollutePixelColorIndex];
            if (transparency == 0)
                continue;
            data[i + 0] = blue;
            data[i + 1] = green;
            data[i + 2] = red;
            data[i + 3] = transparency;
        }
    }

    // Usos da interface

    /// <summary>
    /// Menor retangulo com pixels visiveis (alpha &gt;= 16), ignorando a area <paramref name="ignore"/> (icone de brilho).
    /// Vazio se nao houver nenhum.
    /// </summary>
    public Rectangle GetOpaqueBounds(Rectangle ignore = default)
    {
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        var p = Pixels;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (p[(((y * Width) + x) * 4) + 3] < 16 || ignore.Contains(x, y))
                    continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        return maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    public SpriteImage Crop(Rectangle r)
    {
        var result = new SpriteImage(r.Width, r.Height);
        for (int y = 0; y < r.Height; y++)
            Array.Copy(Pixels, (((r.Y + y) * Width) + r.X) * 4, result.Pixels, y * r.Width * 4, r.Width * 4);
        return result;
    }

    /// <summary>Amplia <paramref name="scale"/> vezes repetindo cada pixel (nearest neighbor).</summary>
    public SpriteImage Scale(int scale)
    {
        if (scale == 1)
            return Clone();
        int w = Width * scale, h = Height * scale;
        var result = new SpriteImage(w, h);
        var src = MemoryMarshal.Cast<byte, int>(Pixels.AsSpan());
        var dst = MemoryMarshal.Cast<byte, int>(result.Pixels.AsSpan());
        for (int y = 0; y < h; y++)
        {
            int sy = y / scale;
            var row = dst.Slice(y * w, w);
            for (int x = 0; x < w; x++)
                row[x] = src[(sy * Width) + (x / scale)];
        }
        return result;
    }
}
