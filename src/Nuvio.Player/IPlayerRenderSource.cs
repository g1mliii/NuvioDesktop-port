namespace Nuvio.Player;

/// <summary>
/// Optional render surface exposed by an <see cref="IPlayerEngine"/> implementation. UI code can bind to
/// this contract without depending on the concrete player backend.
/// </summary>
public interface IPlayerRenderSource
{
    event EventHandler<PlayerRenderFailureEventArgs>? RenderFailed;

    IPlayerRenderSession CreateRenderSession(Func<string, IntPtr> getProcAddress, Action onUpdate);

    void ReportRenderFailure(Exception exception);
}

public interface IPlayerRenderSession : IDisposable
{
    bool Update();

    void Render(int framebufferObject, int width, int height);
}

public sealed class PlayerRenderFailureEventArgs : EventArgs
{
    public PlayerRenderFailureEventArgs(Exception exception)
    {
        Exception = exception;
    }

    public Exception Exception { get; }
}
