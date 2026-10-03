using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

namespace PKHeX.Modern;

public sealed class App : Application
{
    public static System.Action? MobileExit { get; set; }
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Sprites sao pixel art. O SpriteService ja os entrega ampliados 4x com nearest neighbor; na tela, a reducao
        // final usa bilinear simples (LowQuality, sem mipmaps): pixels nitidos e do mesmo tamanho em qualquer escala.
        // Com None puro, escalas nao inteiras (ex.: 125% do Windows) deixam linhas de pixel duplicadas e o sprite torto.
        // RenderOptions nao pode ser definido em estilo, por isso o tratador de classe vale para toda Image.
        Avalonia.Controls.Image.SourceProperty.Changed.AddClassHandler<Avalonia.Controls.Image>((img, _) =>
            Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(img, Avalonia.Media.Imaging.BitmapInterpolationMode.LowQuality));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = Services.AppSettings.Load();
            // Idioma antes de criar qualquer tela: os tratadores traduzem os textos conforme aparecem.
            Services.Loc.Load(settings.UiLanguage);
            Services.Loc.Hook();
            RequestedThemeVariant = settings.DarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
            Theme.AppTheme.SetFont(settings.FontKey);
            Theme.AppTheme.Apply(Theme.AppTheme.Find(settings.ThemeKey));
            Theme.AccentTheme.Apply(Theme.AccentTheme.Find(settings.AccentColor));
            var vm = new MainViewModel(settings);
            Services.CrashLog.Install(msg => vm.Status = msg);
            if (desktop.Args is [{ } path, ..] && System.IO.File.Exists(path))
                vm.Open(path);
            else if (settings.OpenLastSaveOnStartup && vm.HasLastSave)
                vm.Open(settings.LastSavePath!);
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }
        if (ApplicationLifetime is ISingleViewApplicationLifetime mobile)
        {
            var settings = Services.AppSettings.Load();
            settings.CheckForUpdates = false;
            settings.AutoUpdate = false;
            settings.ExternalBankFolders.Clear();
            var root = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "PKHeX.Modern");
            settings.SavesFolder = System.IO.Path.Combine(root, "documents");
            Services.SaveBackup.Folder = System.IO.Path.Combine(root, "backups");
            Services.BankStorage.Root = System.IO.Path.Combine(root, "bank");
            Services.Loc.Load(settings.UiLanguage);
            Services.Loc.Hook();
            RequestedThemeVariant = settings.DarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
            Theme.AppTheme.SetFont(settings.FontKey);
            Theme.AppTheme.Apply(Theme.AppTheme.Find(settings.ThemeKey));
            Theme.AccentTheme.Apply(Theme.AccentTheme.Find(settings.AccentColor));
            var vm = new MainViewModel(settings);
            Services.CrashLog.Install(msg => vm.Status = msg);
            MainView.Documents = new Services.MobileDocuments(root);
            mobile.MainView = new MobileShell(vm);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Alterna entre claro e escuro em tempo real. Retorna true se ficou escuro.</summary>
    public static bool ToggleTheme()
    {
        if (Current is not { } app)
            return true;
        var dark = app.ActualThemeVariant != ThemeVariant.Dark;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        return dark;
    }
}
