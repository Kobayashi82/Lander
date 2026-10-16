using System.IO;

namespace Lander.App.Services;

/// <summary>Carpetas de datos del usuario.</summary>
internal static class AppPaths
{
    /// <summary>%APPDATA%\Lander: ajustes y progreso (viajan con el perfil).</summary>
    public static string RoamingData { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lander");

    /// <summary>%LOCALAPPDATA%\Lander: registros y cachés.</summary>
    public static string LocalData { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lander");

    public static string SettingsFile => Path.Combine(RoamingData, "settings.json");

    public static string ProgressFile => Path.Combine(RoamingData, "progress.json");

    public static string LogDirectory => Path.Combine(LocalData, "logs");

    /// <summary>Ruta del ejecutable (también en publicación de archivo único).</summary>
    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Lander.exe");
}
