namespace Lander.App.Services;

/// <summary>
/// Garantiza una sola instancia por sesión. Una segunda instancia avisa a la primera (que abre sus
/// ajustes o se cierra con <c>--exit</c>) y termina.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string Id = "Lander.Kobayashi82.{6F0B6E1C-6A0B-4B6D-9C3A-2B1E7B4C9D10}";
    private const string ActivateName = @"Local\" + Id + ".Activate";
    private const string ExitName = @"Local\" + Id + ".Exit";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly EventWaitHandle _exit;
    private readonly RegisteredWaitHandle _activateRegistration;
    private readonly RegisteredWaitHandle _exitRegistration;

    private SingleInstance(Mutex mutex, Action onActivate, Action onExit)
    {
        _mutex = mutex;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
        _exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitName);
        _activateRegistration = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
        _exitRegistration = ThreadPool.RegisterWaitForSingleObject(_exit, (_, _) => onExit(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>
    /// Intenta convertirse en la instancia principal. Devuelve null si ya había otra (a la que se avisa
    /// para que muestre sus ajustes).
    /// </summary>
    public static SingleInstance? TryAcquire(Action onActivate, Action onExit)
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + Id + ".Mutex", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            Signal(ActivateName);
            return null;
        }

        return new SingleInstance(mutex, onActivate, onExit);
    }

    /// <summary>Pide a la instancia en marcha que se cierre. Devuelve false si no había ninguna.</summary>
    public static bool RequestExit() => Signal(ExitName);

    public void Dispose()
    {
        _activateRegistration.Unregister(null);
        _exitRegistration.Unregister(null);
        _activate.Dispose();
        _exit.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Liberado desde otro hilo: el sistema lo libera al salir.
        }

        _mutex.Dispose();
    }

    private static bool Signal(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var handle))
        {
            return false;
        }

        using (handle)
        {
            handle.Set();
        }

        return true;
    }
}
