using Nuvio.Core.Models;

namespace Nuvio.Core.Metadata;

public interface IMetadataCache
{
    bool TryGetDetails(string type, string id, out MediaDetails details);

    void SetDetails(string type, string id, MediaDetails details);

    bool TryGetSubtitles(string type, string id, out IReadOnlyList<SubtitleTrack> subtitles);

    void SetSubtitles(string type, string id, IReadOnlyList<SubtitleTrack> subtitles);

    void Clear();
}
