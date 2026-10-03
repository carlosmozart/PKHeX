extern alias gdi;

using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using PKHeX.Core;
using PKHeX.Modern.Sprites;
using GBitmap = System.Drawing.Bitmap;
using GSprite = gdi::PKHeX.Drawing.PokeSprite;
using GMisc = gdi::PKHeX.Drawing.Misc;

// Paridade dos sprites: gera cada sprite pelo PKHeX original (System.Drawing) e pelo porte (PKHeX.Modern.Sprites)
// e compara pixel a pixel. "Visual" = mesmo alpha e mesma cor pre-multiplicada (o que aparece na tela); "exato" =
// mesmos bytes. Saves de saves/ sao so lidos em memoria (nada e gravado).
// Uso: dotnet run -- [pasta de saida] [pasta dos saves]
var outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-spriteparity");
var savesDir = args.Length > 1 ? args[1] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../saves"));
Directory.CreateDirectory(outDir);

GSprite.SpriteName.AllowShinySprite = true;
PKHeX.Drawing.PokeSprite.SpriteName.AllowShinySprite = true;

var stats = new Dictionary<string, (int Total, int Exact, int Visual, int MaxDiff)>();
int saved = 0;

byte[] Bytes(GBitmap bmp)
{
    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    try
    {
        var result = new byte[bmp.Width * bmp.Height * 4];
        for (int y = 0; y < bmp.Height; y++)
            Marshal.Copy(data.Scan0 + (y * data.Stride), result, y * bmp.Width * 4, bmp.Width * 4);
        return result;
    }
    finally { bmp.UnlockBits(data); }
}

void Save(string file, int w, int h, byte[] px)
{
    using var bmp = new GBitmap(w, h, PixelFormat.Format32bppArgb);
    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
    for (int y = 0; y < h; y++)
        Marshal.Copy(px, y * w * 4, data.Scan0 + (y * data.Stride), w * 4);
    bmp.UnlockBits(data);
    bmp.Save(file, ImageFormat.Png);
}

void Compare(string group, string name, GBitmap? g, SpriteImage? o)
{
    var s = stats.GetValueOrDefault(group);
    s.Total++;
    if (g is null || o is null)
    {
        if (g is null && o is null) { s.Exact++; s.Visual++; }
        else Console.WriteLine($"  [{group}] {name}: só um lado existe (original={(g is null ? "não" : "sim")}, porte={(o is null ? "não" : "sim")})");
        stats[group] = s;
        return;
    }
    if (g.Width != o.Width || g.Height != o.Height)
    {
        Console.WriteLine($"  [{group}] {name}: tamanho {g.Width}x{g.Height} x {o.Width}x{o.Height}");
        stats[group] = s;
        return;
    }
    var a = Bytes(g);
    var b = o.Pixels;
    bool exact = a.AsSpan().SequenceEqual(b);
    int maxDiff = 0;
    for (int i = 0; i < a.Length; i += 4)
    {
        int aa = a[i + 3], ba = b[i + 3];
        maxDiff = Math.Max(maxDiff, Math.Abs(aa - ba));
        for (int c = 0; c < 3; c++)
            maxDiff = Math.Max(maxDiff, Math.Abs((a[i + c] * aa / 255) - (b[i + c] * ba / 255)));
    }
    if (exact) s.Exact++;
    if (maxDiff == 0) s.Visual++;
    s.MaxDiff = Math.Max(s.MaxDiff, maxDiff);
    stats[group] = s;
    if (maxDiff > 0 && saved < 40)
    {
        saved++;
        var safe = string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        Save(Path.Combine(outDir, $"{group}-{safe}-original.png"), g.Width, g.Height, a);
        Save(Path.Combine(outDir, $"{group}-{safe}-porte.png"), o.Width, o.Height, b);
        Console.WriteLine($"  [{group}] {name}: diferença visual até {maxDiff}");
    }
}

// 1) Todos os PNGs embutidos
foreach (var name in SpriteResources.AllNames.Order())
{
    var g = (GBitmap?)GSprite.Properties.Resources.ResourceManager.GetObject(name) ?? (GBitmap?)GMisc.Properties.Resources.ResourceManager.GetObject(name);
    Compare("recursos", name, g, SpriteResources.Get(name));
    g?.Dispose();
}
Console.WriteLine($"recursos: {SpriteResources.Count} PNGs embutidos");

// 2) Sprites por especie/forma/genero/shiny/item/ovo nos tres estilos
var builders = new (string Name, GSprite.SpriteBuilder G, PKHeX.Modern.Sprites.SpriteBuilder O)[]
{
    ("classico", GSprite.SpriteUtil.SB8s, PKHeX.Modern.Sprites.SpriteUtil.Classic),
    ("arte", GSprite.SpriteUtil.SB8a, PKHeX.Modern.Sprites.SpriteUtil.Artwork),
    ("circulo", GSprite.SpriteUtil.SB8c, PKHeX.Modern.Sprites.SpriteUtil.Circle),
};
var rnd = new Random(1234);
int[] someItems = [1, 4, 50, 112, 135, 221, 234, 328, 420, 537, 1120, 1606, 2401, 9999];
foreach (var (bname, gb, ob) in builders)
{
    for (ushort species = 0; species <= 1025; species++)
    {
        int forms = species is 0 or > 1025 ? 1 : Math.Max(Math.Max(PersonalTable.SV.GetFormEntry(species, 0).FormCount, PersonalTable.SWSH.GetFormEntry(species, 0).FormCount),
            Math.Max(PersonalTable.USUM.GetFormEntry(species, 0).FormCount, PersonalTable.LA.GetFormEntry(species, 0).FormCount));
        if (species == (int)Species.Alcremie) forms = 9;
        for (byte form = 0; form < forms; form++)
        {
            for (byte gender = 0; gender < 2; gender++)
            {
                foreach (var shiny in new[] { Shiny.Never, Shiny.Always })
                {
                    uint formarg = species == (int)Species.Alcremie ? (uint)rnd.Next(7) : 0;
                    var label = $"{species}-{form}-{gender}-{shiny}-{formarg}";
                    Compare(bname, label, gb.GetSprite(species, form, gender, formarg, 0, false, shiny, EntityContext.Gen8),
                        ob.GetSprite(species, form, gender, formarg, 0, false, shiny, EntityContext.Gen8));
                }
            }
            // item segurado e ovo (com e sem item), em contexto variado
            var ctx = new[] { EntityContext.Gen3, EntityContext.Gen4, EntityContext.Gen6, EntityContext.Gen7, EntityContext.Gen8, EntityContext.Gen9, EntityContext.Gen9a }[rnd.Next(7)];
            int item = someItems[rnd.Next(someItems.Length)];
            Compare(bname, $"{species}-{form}-item{item}-{ctx}", gb.GetSprite(species, form, 0, 0, item, false, Shiny.AlwaysSquare, ctx), ob.GetSprite(species, form, 0, 0, item, false, Shiny.AlwaysSquare, ctx));
            Compare(bname, $"{species}-{form}-ovo-{ctx}", gb.GetSprite(species, form, 0, 0, 0, true, Shiny.Never, ctx), ob.GetSprite(species, form, 0, 0, 0, true, Shiny.Never, ctx));
            Compare(bname, $"{species}-{form}-ovo-item-{ctx}", gb.GetSprite(species, form, 0, 0, item, true, Shiny.Always, ctx), ob.GetSprite(species, form, 0, 0, item, true, Shiny.Always, ctx));
        }
    }
    // casos especiais: Pikachu cosplay (Gen 6), Xerneas no Z-A, Arceus ??? (Gen 4), formas Totem (Gen 7), forma inexistente
    foreach (var (sp, f, ctx) in new (ushort, byte, EntityContext)[] { (25, 3, EntityContext.Gen6), (716, 0, EntityContext.Gen9a), (493, 9, EntityContext.Gen4), (493, 12, EntityContext.Gen4),
        (735, 1, EntityContext.Gen7), (758, 1, EntityContext.Gen7), (778, 2, EntityContext.Gen7), (105, 2, EntityContext.Gen7), (25, 8, EntityContext.Gen7b), (133, 1, EntityContext.Gen7b), (25, 30, EntityContext.Gen8), (151, 5, EntityContext.Gen8) })
    {
        foreach (var shiny in new[] { Shiny.Never, Shiny.Always })
            Compare(bname, $"especial-{sp}-{f}-{ctx}-{shiny}", gb.GetSprite(sp, f, 0, 0, 0, false, shiny, ctx), ob.GetSprite(sp, f, 0, 0, 0, false, shiny, ctx));
    }
    // itens (icone do item segurado / mochila)
    foreach (var ctx in new[] { EntityContext.Gen8, EntityContext.Gen9, EntityContext.Gen9a })
    {
        for (int item = 0; item <= 2700; item++)
            Compare($"{bname}-itens", $"{item}-{ctx}", gb.GetItemSprite(item, ctx), ob.GetItemSprite(item, ctx));
    }
}

// 3) Bolas
for (byte ball = 0; ball <= 40; ball++)
    Compare("bolas", ball.ToString(), GSprite.SpriteUtil.GetBallSprite(ball), PKHeX.Modern.Sprites.SpriteUtil.GetBallSprite(ball));

// 4) Papeis de parede: nome por versao e indice
int wpNames = 0, wpNameDiff = 0;
foreach (var v in Enum.GetValues<GameVersion>())
{
    for (int i = 0; i < 34; i++)
    {
        wpNames++;
        var gn = GMisc.WallpaperUtil.GetWallpaperResourceName(v, i);
        var on = PKHeX.Modern.Sprites.WallpaperUtil.GetWallpaperResourceName(v, i);
        if (gn != on) { wpNameDiff++; Console.WriteLine($"  papel de parede {v} {i}: {gn} x {on}"); }
    }
}
Console.WriteLine($"papéis de parede: {wpNames - wpNameDiff}/{wpNames} nomes iguais");

// 5) Pokemon de verdade: sombra, Gigantamax, Alpha, Deoxys da Gen 3, slots com marcas (saves reais, so leitura)
void ComparePk(string group, string name, PKM pk, SaveFile? sav = null, int box = -1, int slot = -1)
{
    if (sav is null)
        Compare(group, name, GSprite.SpriteUtil.Sprite(pk), PKHeX.Modern.Sprites.SpriteUtil.GetSprite(pk));
    else
        Compare(group, name, GSprite.SpriteUtil.Sprite(pk, sav, box, slot), PKHeX.Modern.Sprites.SpriteUtil.GetSprite(pk, sav, box, slot));
}
void Init(SaveFile sav)
{
    GSprite.SpriteUtil.Initialize(sav);
    PKHeX.Modern.Sprites.SpriteUtil.Initialize(sav);
}

Init(BlankSaveFile.Get(GameVersion.SW));
var xk3 = new XK3 { Species = (int)Species.Lugia, ShadowID = 1, Purification = -100 };
ComparePk("pkm", "Lugia sombrio", xk3);
var ck3 = new CK3 { Species = (int)Species.Makuhita, ShadowID = 1, Purification = -100 };
ComparePk("pkm", "Makuhita sombrio", ck3);
var pk8 = new PK8 { Species = (int)Species.Charizard, CanGigantamax = true, HeldItem = 1 };
ComparePk("pkm", "Charizard Gigantamax", pk8);
pk8.SetShinySID(Shiny.AlwaysSquare);
ComparePk("pkm", "Charizard Gigantamax shiny", pk8);
var pa8 = new PA8 { Species = (int)Species.Kleavor, IsAlpha = true };
Init(BlankSaveFile.Get(GameVersion.PLA));
ComparePk("pkm", "Kleavor Alpha (circulo)", pa8);
foreach (var v in new[] { GameVersion.E, GameVersion.FR, GameVersion.LG })
{
    Init(BlankSaveFile.Get(v));
    ComparePk("pkm", $"Deoxys {v}", new PK3 { Species = (int)Species.Deoxys });
}

int realSaves = 0;
if (Directory.Exists(savesDir))
{
    foreach (var file in Directory.EnumerateFiles(savesDir, "*", SearchOption.AllDirectories).Where(f => new FileInfo(f).Length < 4_000_000))
    {
        if (!SaveUtil.TryGetSaveFile(File.ReadAllBytes(file), out var sav))
            continue;
        realSaves++;
        Init(sav);
        var name = Path.GetFileName(file);
        for (int b = 0; b < sav.BoxCount; b++)
        {
            for (int s = 0; s < sav.BoxSlotCount; s++)
            {
                var pk = sav.GetBoxSlotAtIndex(b, s);
                if (pk.Species == 0 && s % 7 != 0)
                    continue;
                ComparePk("slots", $"{name}-{b}-{s}", pk, sav, b, s);
            }
            if (sav is IBoxDetailWallpaper)
                Compare("papel-de-parede", $"{name}-{b}", GMisc.WallpaperUtil.WallpaperImage(sav, b), PKHeX.Modern.Sprites.WallpaperUtil.GetWallpaper(sav, b));
        }
        foreach (var pk in sav.PartyData)
            ComparePk("equipe", $"{name}-{pk.Species}", pk);
    }
}
Console.WriteLine($"saves reais lidos (só leitura): {realSaves}");

Console.WriteLine();
Console.WriteLine($"{"grupo",-18}{"total",8}{"exatos",8}{"visual",8}  maior diferença");
bool ok = true;
foreach (var (group, s) in stats.OrderBy(k => k.Key))
{
    Console.WriteLine($"{group,-18}{s.Total,8}{s.Exact,8}{s.Visual,8}  {s.MaxDiff}");
    if (s.Visual != s.Total) ok = false;
}
Console.WriteLine(ok ? "TUDO OK (visualmente idênticos)" : $"DIFERENÇAS (amostras em {outDir})");
return ok ? 0 : 1;
