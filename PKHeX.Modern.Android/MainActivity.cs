using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;

namespace PKHeX.Modern.Android;

[Activity(Label = "PKHeX Modern", Theme = "@style/MyTheme.NoActionBar", MainLauncher = true, Exported = true,
    // Paisagem: a interface e a mesma do desktop (barra lateral, paginas e editor lado a lado).
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        App.MobileExit = Finish;
    }
    // Simbolos e emoji que a Inter nao tem vem de fontes embutidas (Noto, OFL): a busca na fonte de emoji do sistema
    // desenhava "≣" no lugar de ♂/♀ e dos icones dos botoes.
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont()
        .With(new FontManagerOptions
        {
            FontFallbacks =
            [
                new FontFallback { FontFamily = new FontFamily("avares://PKHeX.Modern.UI/Assets/Fonts#Noto Sans Symbols 2") },
                new FontFallback { FontFamily = new FontFamily("avares://PKHeX.Modern.UI/Assets/Fonts#Noto Emoji") },
            ],
        });
}
