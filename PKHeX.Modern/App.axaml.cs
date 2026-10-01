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
            var vm = new MainViewModel();
            if (desktop.Args is [{ } path, ..] && System.IO.File.Exists(path))
                vm.Open(path);
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Alterna entre claro e escuro em tempo real.</summary>
    public static void ToggleTheme()
    {
        if (Current is not { } app)
            return;
        app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
