using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;

namespace PKHeX.Modern.Android;

[Activity(Label = "PKHeX Modern", Theme = "@style/MyTheme.NoActionBar", MainLauncher = true, Exported = true,
    // Paisagem: a interface e a mesma do desktop (barra lateral, paginas e editor lado a lado).
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        // Controle ou teclado Bluetooth conectando nao reinicia o app (perderia a edicao em andamento).
        | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        App.MobileExit = Finish;
        PKHeX.Modern.Services.AutoUpdater.InstallApk = InstallApk;
    }
    // Simbolos e emoji que a Inter nao tem vem de fontes embutidas (Noto, OFL): a busca na fonte de emoji do sistema
    // desenhava "≣" no lugar de ♂/♀ e dos icones dos botoes.
    /// <summary>Abre o instalador do Android com o APK baixado (o sistema pede para permitir apps desta origem, se preciso).</summary>
    private bool InstallApk(string path)
    {
        try
        {
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(this, PackageName + ".updates", new Java.IO.File(path));
            var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionView);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission | global::Android.Content.ActivityFlags.NewTask);
            StartActivity(intent);
            return true;
        }
        catch (System.Exception ex)
        {
            PKHeX.Modern.Services.CrashLog.Write(ex);
            return false;
        }
    }

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
