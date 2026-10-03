using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace PKHeX.Modern.AndroidPoc;

[Activity(Label = "PKHeX Modern — Android PoC", Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity<App>;
