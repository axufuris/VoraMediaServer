namespace Vora.Infrastructure.Transcoding.Keyframes;

internal static class KeyframeIndex
{
    private const int HeadLength = 12;

    public static IReadOnlyList<double>? TryRead(string path, int videoStreamIndex)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
            Span<byte> head = stackalloc byte[HeadLength];
            if (stream.Read(head) < HeadLength) return null;

            if (MatroskaKeyframes.Matches(head)) return MatroskaKeyframes.Read(stream, videoStreamIndex);
            if (Mp4Keyframes.Matches(head)) return Mp4Keyframes.Read(stream, videoStreamIndex);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }
}
