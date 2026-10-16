using System.IO;
using Lander.App.Services;
using Microsoft.Win32;

namespace Lander.App.Platform;

/// <summary>Inicio con Windows mediante la clave Run del usuario (no requiere permisos de administrador).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lander";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{AppPaths.ExecutablePath}\"");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn("No se pudo actualizar el inicio con Windows.", ex);
        }
    }
}
