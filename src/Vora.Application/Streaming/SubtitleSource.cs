namespace Vora.Application.Streaming;

// Where a subtitle's cues come from. Embedded tracks are addressed by a position
// inside the video container; external ones are a file of their own sitting next
// to it. Everything downstream — which file to fingerprint, which file to hand
// FFmpeg, whether a stream index means anything — follows from that difference,
// so it travels as one value rather than as flags the callers have to agree on.
public sealed record SubtitleSource(string VideoFilePath, string? ExternalFilePath, int StreamIndex, int Ordinal)
{
    public static SubtitleSource Embedded(string videoFilePath, int streamIndex, int ordinal) =>
        new(videoFilePath, null, streamIndex, ordinal);

    public static SubtitleSource External(string videoFilePath, string externalFilePath) =>
        new(videoFilePath, externalFilePath, -1, -1);

    public bool IsExternal => ExternalFilePath != null;

    // The file whose bytes the cues come from, and therefore the one whose size
    // and mtime decide whether a cached VTT is still valid. For a sidecar that is
    // the sidecar itself: re-saving it must invalidate the cache even though the
    // video it sits beside never changed.
    public string ContentPath => ExternalFilePath ?? VideoFilePath;
}
