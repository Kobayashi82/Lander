using System.IO;
using System.Globalization;
using System.Text;

namespace Lander.App.Services;

/// <summary>
/// Registro sencillo a archivo (un archivo por día, se conservan los últimos siete). Seguro entre hilos
/// y sin dependencias; nunca lanza excepciones.
/// </summary>
internal static class Log
{
    private const int KeepFiles = 7;
    private static readonly Lock Gate = new();
    private static StreamWriter? _writer;
    private static DateOnly _day;

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN ", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    [System.Diagnostics.Conditional("DEBUG")]
    public static void Debug(string message) => Write("DEBUG", message, null);

    public static void Shutdown()
    {
        lock (Gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var now = DateTime.Now;
            var line = new StringBuilder()
                .Append(now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(' ').Append(level)
                .Append(" [").Append(Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)).Append("] ")
                .Append(message);
            if (exception is not null)
            {
                line.AppendLine().Append(exception);
            }

            lock (Gate)
            {
                EnsureWriter(DateOnly.FromDateTime(now));
                _writer?.WriteLine(line.ToString());
            }

            System.Diagnostics.Debug.WriteLine(line.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // El registro nunca debe tumbar la aplicación.
        }
    }

    private static void EnsureWriter(DateOnly day)
    {
        if (_writer is not null && day == _day)
        {
            return;
        }

        _writer?.Dispose();
        Directory.CreateDirectory(AppPaths.LogDirectory);
        var path = Path.Combine(AppPaths.LogDirectory, $"lander-{day:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
        {
            AutoFlush = true,
        };
        _day = day;
        PruneOldFiles();
    }

    private static void PruneOldFiles()
    {
        var files = new DirectoryInfo(AppPaths.LogDirectory).GetFiles("lander-*.log")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(KeepFiles);
        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // En uso por otra instancia: ya se borrará otro día.
            }
        }
    }
}
