using System.Runtime.InteropServices;

namespace Nuvio.Player.LibMpv;

/// <summary>
/// Minimal P/Invoke surface for the libmpv client API (<c>mpv/client.h</c>) plus the OpenGL render API
/// (<c>mpv/render.h</c>, <c>mpv/render_gl.h</c>) needed for the embedded engine and render spike.
/// The actual shared library is resolved at runtime by <see cref="LibMpvLibraryLoader"/>, which registers
/// a <see cref="NativeLibrary.SetDllImportResolver"/> mapping <see cref="LibraryName"/> to the loaded handle.
/// </summary>
internal static partial class LibMpvNative
{
    internal const string LibraryName = "mpv";

    internal const string RenderApiTypeOpenGl = "opengl";

    // --- Core client API ---

    [LibraryImport(LibraryName)]
    internal static partial ulong mpv_client_api_version();

    [LibraryImport(LibraryName)]
    internal static partial IntPtr mpv_create();

    [LibraryImport(LibraryName)]
    internal static partial int mpv_initialize(IntPtr ctx);

    [LibraryImport(LibraryName)]
    internal static partial void mpv_terminate_destroy(IntPtr ctx);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_set_option_string(IntPtr ctx, string name, string data);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_set_property_string(IntPtr ctx, string name, string data);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_get_property(IntPtr ctx, string name, MpvFormat format, IntPtr data);

    /// <summary>Returns a libmpv-allocated UTF-8 string that the caller must release with <c>mpv_free</c>.</summary>
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr mpv_get_property_string(IntPtr ctx, string name);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_observe_property(IntPtr ctx, ulong replyUserdata, string name, MpvFormat format);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_request_log_messages(IntPtr ctx, string minLevel);

    /// <summary>args is a NULL-terminated array of UTF-8 <c>char*</c> pointers built by the caller.</summary>
    [LibraryImport(LibraryName)]
    internal static partial int mpv_command(IntPtr ctx, IntPtr args);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [LibraryImport(LibraryName)]
    internal static partial void mpv_wakeup(IntPtr ctx);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr mpv_error_string(int error);

    [LibraryImport(LibraryName)]
    internal static partial void mpv_free(IntPtr data);

    // --- OpenGL render API ---

    [LibraryImport(LibraryName)]
    internal static partial int mpv_render_context_create(out IntPtr res, IntPtr mpv, [In] MpvRenderParam[] parameters);

    [LibraryImport(LibraryName)]
    internal static partial void mpv_render_context_set_update_callback(IntPtr ctx, IntPtr callback, IntPtr callbackCtx);

    [LibraryImport(LibraryName)]
    internal static partial ulong mpv_render_context_update(IntPtr ctx);

    [LibraryImport(LibraryName)]
    internal static partial int mpv_render_context_render(IntPtr ctx, [In] MpvRenderParam[] parameters);

    [LibraryImport(LibraryName)]
    internal static partial void mpv_render_context_free(IntPtr ctx);

    internal static string? ErrorString(int error)
    {
        var ptr = mpv_error_string(error);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }
}

internal enum MpvFormat
{
    None = 0,
    String = 1,
    OsdString = 2,
    Flag = 3,
    Int64 = 4,
    Double = 5,
    Node = 6,
    NodeArray = 7,
    NodeMap = 8,
    ByteArray = 9
}

internal enum MpvEventId
{
    None = 0,
    Shutdown = 1,
    LogMessage = 2,
    GetPropertyReply = 3,
    SetPropertyReply = 4,
    CommandReply = 5,
    StartFile = 6,
    EndFile = 7,
    FileLoaded = 8,
    ClientMessage = 16,
    VideoReconfig = 17,
    AudioReconfig = 18,
    Seek = 20,
    PlaybackRestart = 21,
    PropertyChange = 22,
    QueueOverflow = 24,
    Hook = 25
}

internal enum MpvEndFileReason
{
    Eof = 0,
    Stop = 2,
    Quit = 3,
    Error = 4,
    Redirect = 5
}

internal enum MpvRenderParamType
{
    Invalid = 0,
    ApiType = 1,
    OpenGlInitParams = 2,
    OpenGlFbo = 3,
    FlipY = 4,
    Depth = 5,
    IccProfile = 6,
    AmbientLight = 7,
    X11Display = 8,
    WaylandDisplay = 9,
    AdvancedControl = 10,
    NextFrameInfo = 11,
    BlockForTargetTime = 12,
    SkipRendering = 13
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEvent
{
    public MpvEventId EventId;
    public int Error;
    public ulong ReplyUserData;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventProperty
{
    public IntPtr Name;
    public MpvFormat Format;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventLogMessage
{
    public IntPtr Prefix;
    public IntPtr Level;
    public IntPtr Text;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvRenderParam
{
    public MpvRenderParamType Type;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvOpenGlInitParams
{
    public IntPtr GetProcAddress;
    public IntPtr GetProcAddressContext;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvOpenGlFbo
{
    public int Fbo;
    public int Width;
    public int Height;
    public int InternalFormat;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr MpvGetProcAddressDelegate(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void MpvRenderUpdateDelegate(IntPtr ctx);
