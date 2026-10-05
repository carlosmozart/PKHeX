using System.Collections.Generic;

namespace PKHeX.Modern.ViewModels;

public sealed partial class MainViewModel
{
    public IReadOnlyList<string> WallpaperIntensityOptions { get; } = ["Normal", "Suave", "Desligado"];
    public int WallpaperIntensityIndex
    {
        get => System.Math.Clamp(Settings.WallpaperIntensity, 0, 2);
        set
        {
            if (value is < 0 or > 2 || value == WallpaperIntensityIndex) return;
            Settings.WallpaperIntensity = value; Settings.Save();
            Boxes.WallpaperIntensity = value;
            Raise(nameof(WallpaperIntensityIndex));
        }
    }
}
