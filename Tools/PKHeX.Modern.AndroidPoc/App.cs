using Application = Avalonia.Application;
using Button = Avalonia.Controls.Button;
using ScrollViewer = Avalonia.Controls.ScrollViewer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;
using PKHeX.Core;

namespace PKHeX.Modern.AndroidPoc;

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime mobile)
            mobile.MainView = new SaveView();
        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class SaveView : UserControl
{
    private readonly TextBlock _summary = new() { Text = "Escolha um save para ver o jogo, o treinador e a equipe.", TextWrapping = TextWrapping.Wrap };
    private readonly Button _open = new() { Content = "Abrir save", MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch };

    public SaveView()
    {
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20, 36, 20, 24), Spacing = 20,
                Children =
                {
                    new TextBlock { Text = "PKHeX Modern", FontSize = 26 },
                    new TextBlock { Text = "Protótipo Android • somente leitura", TextWrapping = TextWrapping.Wrap },
                    _open, _summary,
                },
            },
        };
        _open.Click += async (_, _) => await OpenAsync();
    }

    private async Task OpenAsync()
    {
        _open.IsEnabled = false;
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider
                ?? throw new InvalidOperationException("O seletor de arquivos ainda não está disponível.");
            if (!storage.CanOpen)
                throw new InvalidOperationException("Este dispositivo não oferece um seletor de arquivos.");
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Abrir save", AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.All],
            });
            if (files.Count == 0)
                return;
            using var file = files[0];
            await using var input = await file.OpenReadAsync();
            var bytes = await ReadBoundedAsync(input);
            // O Core roda fora da thread da interface; nunca usamos um caminho local para content://.
            _summary.Text = await Task.Run(() => SaveSummary.Describe(bytes, file.Name));
        }
        catch (Exception ex)
        {
            _summary.Text = $"Não foi possível abrir o save: {ex.Message}";
        }
        finally
        {
            _open.IsEnabled = true;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream input)
    {
        const int limit = 64 * 1024 * 1024;
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer)) != 0)
        {
            if (output.Length + count > limit)
                throw new InvalidDataException("O arquivo excede o limite de 64 MB do protótipo.");
            await output.WriteAsync(buffer.AsMemory(0, count));
        }
        return output.ToArray();
    }
}

