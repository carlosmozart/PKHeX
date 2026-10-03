using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace PKHeX.Modern.Sprites;

/// <summary>
/// Os PNGs do PKHeX embutidos neste assembly, pelo nome do recurso do PKHeX ("b_25", "bitem_1", "_ball4"...).
/// Decodifica uma vez com SkiaSharp (BGRA sem pre-multiplicacao, como o System.Drawing) e devolve sempre uma copia,
/// porque a montagem dos sprites altera as imagens.
/// </summary>
public static class SpriteResources
{
    private const string Prefix = "spr.";
    private static readonly HashSet<string> Names = [.. typeof(SpriteResources).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Select(n => n[Prefix.Length..])];
    private static readonly ConcurrentDictionary<string, SpriteImage?> Cache = new();

    /// <summary>Quantos PNGs estao embutidos.</summary>
    public static int Count => Names.Count;
    /// <summary>Nomes de todos os PNGs embutidos.</summary>
    public static IReadOnlyCollection<string> AllNames => Names;

    public static bool Exists(string name) => Names.Contains(name);

    /// <summary>Copia da imagem, ou null se o PKHeX nao tem esse recurso.</summary>
    public static SpriteImage? Get(string name) => Load(name)?.Clone();

    /// <summary>Copia da imagem; falha se nao existir (recursos fixos, como o icone de brilho).</summary>
    public static SpriteImage Required(string name) => Get(name) ?? throw new InvalidOperationException($"sprite '{name}' ausente");

    private static SpriteImage? Load(string name) => !Names.Contains(name) ? null : Cache.GetOrAdd(name, static n =>
    {
        using var stream = typeof(SpriteResources).Assembly.GetManifestResourceStream(Prefix + n);
        if (stream is null)
            return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Decode(ms.ToArray());
    });

    /// <summary>Decodifica um PNG para BGRA sem pre-multiplicacao.</summary>
    public static SpriteImage? Decode(byte[] png)
    {
        using var codec = SKCodec.Create(new MemoryStream(png));
        if (codec is null)
            return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var image = new SpriteImage(info.Width, info.Height);
        unsafe
        {
            fixed (byte* p = image.Pixels)
            {
                var result = codec.GetPixels(info, (IntPtr)p);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                    return null;
            }
        }
        return image;
    }
}
