using System.Runtime.InteropServices;

namespace Nuvio.Platform;

public sealed record LibMpvNativeLibraryLoadResult(
    bool IsAvailable,
    IntPtr Handle,
    string? LibraryPath,
    string? Message);

/// <summary>
/// Performs the actual native libmpv load for the current platform. Player code owns P/Invoke binding, but
/// path discovery and native handle loading stay in Nuvio.Platform.
/// </summary>
public sealed class LibMpvNativeLibraryLoader
{
    private readonly LibMpvLibraryLocator _locator;

    public LibMpvNativeLibraryLoader()
        : this(new LibMpvLibraryLocator())
    {
    }

    public LibMpvNativeLibraryLoader(LibMpvLibraryLocator locator)
    {
        _locator = locator;
    }

    public LibMpvNativeLibraryLoadResult Load()
    {
        var discovery = _locator.Locate();
        if (discovery.Candidates.Count == 0)
        {
            return new LibMpvNativeLibraryLoadResult(false, IntPtr.Zero, null, discovery.Message);
        }

        string? loadedPath = null;
        var attempts = new List<string>();
        foreach (var candidate in discovery.Candidates)
        {
            if (NativeLibrary.TryLoad(candidate.NameOrPath, out var handle))
            {
                loadedPath = candidate.NameOrPath;
                return new LibMpvNativeLibraryLoadResult(true, handle, loadedPath, null);
            }

            attempts.Add(candidate.NameOrPath);
        }

        var detail = attempts.Count == 0 ? string.Empty : $" Tried: {string.Join(", ", attempts)}.";
        return new LibMpvNativeLibraryLoadResult(
            false,
            IntPtr.Zero,
            loadedPath,
            $"libmpv could not be loaded.{detail} Install libmpv or set NUVIO_LIBMPV_PATH; the app will use external mpv instead.");
    }
}
