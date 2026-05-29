namespace Nuvio.Core.Settings;

public interface ISettingsStore
{
    Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(DesktopSettings settings, CancellationToken cancellationToken);
}
