using System.Runtime.InteropServices;

namespace Nuvio.Player.LibMpv;

/// <summary>
/// Wraps an <c>mpv_render_context</c> using the OpenGL render API. Created by <see cref="LibMpvEngine"/> and
/// driven by the Avalonia render host. Keeps the libmpv interop types internal while exposing a managed
/// surface: the host supplies a GL proc-address resolver and an update callback, then calls
/// <see cref="Update"/>/<see cref="Render"/> from its GL render thread. This is the Phase 6 render spike;
/// callers must treat creation failure as a graceful degrade (audio/events still work, external mpv remains
/// the fallback).
/// </summary>
public sealed class LibMpvRenderSession : IPlayerRenderSession
{
    private const ulong RenderUpdateFrame = 1; // MPV_RENDER_UPDATE_FRAME

    // Native callbacks must outlive the render context, so keep the delegates rooted.
    private readonly MpvGetProcAddressDelegate _getProcAddress;
    private readonly MpvRenderUpdateDelegate _updateCallback;
    private readonly Action _onUpdate;

    // Serializes the libmpv render-context calls. Update/Render run on the GL render thread while Dispose
    // (engine teardown) can run on another thread; the render API forbids concurrent calls on one context,
    // so the lock guarantees a free never overlaps an in-flight render (use-after-free).
    private readonly object _sync = new();

    private IntPtr _context;
    private IntPtr _apiTypeString;
    private IntPtr _fboPtr;
    private IntPtr _flipPtr;
    private MpvRenderParam[] _renderParameters = [];
    private bool _isDisposed;

    internal LibMpvRenderSession(IntPtr mpvHandle, Func<string, IntPtr> getProcAddress, Action onUpdate)
    {
        ArgumentNullException.ThrowIfNull(getProcAddress);
        _onUpdate = onUpdate ?? throw new ArgumentNullException(nameof(onUpdate));
        _getProcAddress = (_, name) => getProcAddress(name);
        _updateCallback = OnRenderUpdate;

        try
        {
            _apiTypeString = Marshal.StringToHGlobalAnsi(LibMpvNative.RenderApiTypeOpenGl);

            var initParams = new MpvOpenGlInitParams
            {
                GetProcAddress = Marshal.GetFunctionPointerForDelegate(_getProcAddress),
                GetProcAddressContext = IntPtr.Zero
            };

            var initParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlInitParams>());
            try
            {
                Marshal.StructureToPtr(initParams, initParamsPtr, false);

                var parameters = new[]
                {
                    new MpvRenderParam { Type = MpvRenderParamType.ApiType, Data = _apiTypeString },
                    new MpvRenderParam { Type = MpvRenderParamType.OpenGlInitParams, Data = initParamsPtr },
                    new MpvRenderParam { Type = MpvRenderParamType.Invalid, Data = IntPtr.Zero }
                };

                var result = LibMpvNative.mpv_render_context_create(out _context, mpvHandle, parameters);
                if (result < 0)
                {
                    var reason = LibMpvNative.ErrorString(result) ?? "unknown error";
                    throw new InvalidOperationException($"mpv_render_context_create failed: {reason}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(initParamsPtr);
            }

            _fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlFbo>());
            _flipPtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(_flipPtr, 1); // flip Y: GL framebuffers are bottom-up
            _renderParameters =
            [
                new MpvRenderParam { Type = MpvRenderParamType.OpenGlFbo, Data = _fboPtr },
                new MpvRenderParam { Type = MpvRenderParamType.FlipY, Data = _flipPtr },
                new MpvRenderParam { Type = MpvRenderParamType.Invalid, Data = IntPtr.Zero }
            ];

            LibMpvNative.mpv_render_context_set_update_callback(
                _context,
                Marshal.GetFunctionPointerForDelegate(_updateCallback),
                IntPtr.Zero);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Returns true when mpv has a new frame ready to render.</summary>
    public bool Update()
    {
        lock (_sync)
        {
            if (_isDisposed || _context == IntPtr.Zero)
            {
                return false;
            }

            return (LibMpvNative.mpv_render_context_update(_context) & RenderUpdateFrame) != 0;
        }
    }

    /// <summary>Renders the current frame into the given OpenGL framebuffer object.</summary>
    public void Render(int framebufferObject, int width, int height)
    {
        lock (_sync)
        {
            if (_isDisposed || _context == IntPtr.Zero)
            {
                return;
            }

            RenderLocked(framebufferObject, width, height);
        }
    }

    private void RenderLocked(int framebufferObject, int width, int height)
    {
        var fbo = new MpvOpenGlFbo
        {
            Fbo = framebufferObject,
            Width = width,
            Height = height,
            InternalFormat = 0
        };

        Marshal.StructureToPtr(fbo, _fboPtr, false);
        LibMpvNative.mpv_render_context_render(_context, _renderParameters);
    }

    private void OnRenderUpdate(IntPtr ctx)
    {
        if (!_isDisposed)
        {
            _onUpdate();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (_context != IntPtr.Zero)
            {
                LibMpvNative.mpv_render_context_free(_context);
                _context = IntPtr.Zero;
            }

            if (_fboPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_fboPtr);
                _fboPtr = IntPtr.Zero;
            }

            if (_flipPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_flipPtr);
                _flipPtr = IntPtr.Zero;
            }

            if (_apiTypeString != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_apiTypeString);
                _apiTypeString = IntPtr.Zero;
            }

            _renderParameters = [];
        }
    }
}
