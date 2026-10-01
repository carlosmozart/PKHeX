using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace PKHeX.Modern.Services;

/// <summary>
/// Rede de seguranca: erros nao tratados vao para %APPDATA%\PKHeX.Modern\crash.log e,
/// quando acontecem na thread da interface, viram uma mensagem na barra de status em vez de fechar o app.
/// </summary>
public static class CrashLog
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKHeX.Modern", "crash.log");

    /// <summary>Instala os handlers. <paramref name="report"/> recebe a mensagem curta para a interface.</summary>
    public static void Install(Action<string> report)
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Write(e.Exception);
            e.Handled = true; // o app continua aberto
            report($"Erro inesperado: {e.Exception.Message} (detalhes em {FilePath})");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Write(ex);
        };
    }

    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // sem onde gravar: ignora
        }
    }
}
