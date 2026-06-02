using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Nuvio.Player;

namespace Nuvio.Desktop.Controls;

/// <summary>
/// Phase 6 render spike: hosts embedded libmpv video inside an Avalonia OpenGL surface using the libmpv
/// OpenGL render API. Bind <see cref="Engine"/> to the active render source; the control is inert (and
/// harmless) when the engine is external mpv or null.
/// Render-context creation failures degrade gracefully — playback audio/events continue and external mpv
/// remains the fallback, matching the Phase 6 "never strand the user" rule.
/// </summary>
public sealed class LibMpvVideoView : OpenGlControlBase
{
    public static readonly StyledProperty<IPlayerRenderSource?> EngineProperty =
        AvaloniaProperty.Register<LibMpvVideoView, IPlayerRenderSource?>(nameof(Engine));

    private IPlayerRenderSession? _session;
    private IPlayerRenderSource? _attachedEngine;
    private int _frameRequestQueued;
    private bool _renderFailed;

    public IPlayerRenderSource? Engine
    {
        get => GetValue(EngineProperty);
        set => SetValue(EngineProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EngineProperty)
        {
            // Engine swapped or detached. Release the old render session immediately so a hidden control
            // cannot keep an old engine/session alive waiting for a render pass that may never be scheduled.
            _renderFailed = false;
            if (!ReferenceEquals(_attachedEngine, Engine))
            {
                DisposeSession();
            }

            QueueFrameRendering();
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl) => DisposeSession();

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        var engine = Engine;

        // Reconcile session ownership on the GL thread (the context is current here, so freeing is safe).
        if (!ReferenceEquals(_attachedEngine, engine))
        {
            DisposeSession();
        }

        if (engine is null || _renderFailed)
        {
            return;
        }

        if (_session is null)
        {
            if (!TryCreateSession(engine, gl.GetProcAddress))
            {
                return;
            }
        }

        var size = GetPixelSize();
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        _session!.Update();
        _session.Render(fb, size.Width, size.Height);
    }

    internal bool TryCreateSessionForTesting(IPlayerRenderSource engine, Func<string, IntPtr> getProcAddress) =>
        TryCreateSession(engine, getProcAddress);

    private bool TryCreateSession(IPlayerRenderSource engine, Func<string, IntPtr> getProcAddress)
    {
        try
        {
            _session = engine.CreateRenderSession(
                getProcAddress,
                QueueFrameRendering);
            _attachedEngine = engine;
            return true;
        }
        catch (Exception ex)
        {
            // Spike degrade path: leave video blank, keep audio/events on the engine, stop retrying.
            _renderFailed = true;
            DisposeSession();
            engine.ReportRenderFailure(ex);
            return false;
        }
    }

    private PixelSize GetPixelSize()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        return PixelSize.FromSize(Bounds.Size, scaling);
    }

    private void DisposeSession()
    {
        Interlocked.Exchange(ref _frameRequestQueued, 0);
        _session?.Dispose();
        _session = null;
        _attachedEngine = null;
    }

    private void QueueFrameRendering()
    {
        if (Interlocked.Exchange(ref _frameRequestQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _frameRequestQueued, 0);
            RequestNextFrameRendering();
        });
    }
}
