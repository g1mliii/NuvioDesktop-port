using System.Runtime.InteropServices;
using Nuvio.Platform;

namespace Nuvio.Player.LibMpv;

public sealed record LibMpvLoadResult(
    bool IsAvailable,
    string? LibraryPath,
    string? Version,
    string? Message);

/// <summary>
/// Wires the platform-loaded libmpv handle to <see cref="LibMpvNative"/> through a
/// <see cref="NativeLibrary.SetDllImportResolver"/> hook. Native path discovery and handle loading live in
/// <see cref="LibMpvNativeLibraryLoader"/> so platform-specific loading stays in <c>Nuvio.Platform</c>.
/// </summary>
public static class LibMpvLibraryLoader
{
    private static readonly object Gate = new();
    private static IntPtr _handle;
    private static bool _resolverRegistered;
    private static LibMpvLoadResult? _result;

    /// <summary>
    /// Attempts to load libmpv. Idempotent: the first successful load is cached and reused. Safe to call
    /// from the engine factory as an availability probe before constructing <see cref="LibMpvEngine"/>.
    /// </summary>
    public static LibMpvLoadResult EnsureLoaded() => EnsureLoaded(new LibMpvNativeLibraryLoader());

    public static LibMpvLoadResult EnsureLoaded(LibMpvLibraryLocator locator)
        => EnsureLoaded(new LibMpvNativeLibraryLoader(locator));

    public static LibMpvLoadResult EnsureLoaded(LibMpvNativeLibraryLoader nativeLoader)
    {
        lock (Gate)
        {
            // First result wins, success or failure: a negative probe walks every candidate directory and
            // attempts dlopen on each bare name, so re-running it on every Create()/InitializeAsync() call
            // (libmpv absent) is wasted disk + loader work.
            if (_result is not null)
            {
                return _result;
            }

            var load = nativeLoader.Load();
            if (!load.IsAvailable || load.Handle == IntPtr.Zero)
            {
                return _result = new LibMpvLoadResult(
                    false,
                    null,
                    null,
                    load.Message ?? "libmpv could not be loaded; the app will use external mpv instead.");
            }

            _handle = load.Handle;
            RegisterResolver(_handle);

            string? version = null;
            try
            {
                version = FormatVersion(LibMpvNative.mpv_client_api_version());
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            return _result = new LibMpvLoadResult(true, load.LibraryPath, version, null);
        }
    }

    private static void RegisterResolver(IntPtr handle)
    {
        if (_resolverRegistered)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(LibMpvNative).Assembly, (name, _, _) =>
            string.Equals(name, LibMpvNative.LibraryName, StringComparison.Ordinal) ? handle : IntPtr.Zero);
        _resolverRegistered = true;
    }

    private static string FormatVersion(ulong apiVersion)
    {
        var major = (apiVersion >> 16) & 0xFFFF;
        var minor = apiVersion & 0xFFFF;
        return $"client API {major}.{minor}";
    }
}
