using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

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
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont();
}
