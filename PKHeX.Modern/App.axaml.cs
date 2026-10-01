using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

namespace PKHeX.Modern;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = Services.AppSettings.Load();
            RequestedThemeVariant = settings.DarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
            var vm = new MainViewModel(settings);
            Services.CrashLog.Install(msg => vm.Status = msg);
            if (desktop.Args is [{ } path, ..] && System.IO.File.Exists(path))
                vm.Open(path);
            else if (settings.OpenLastSaveOnStartup && vm.HasLastSave)
                vm.Open(settings.LastSavePath!);
            desktop.MainWindow = new MainWindow { DataContext = vm };
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
