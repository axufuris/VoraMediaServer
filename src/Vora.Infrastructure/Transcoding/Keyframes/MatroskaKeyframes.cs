namespace Vora.Infrastructure.Transcoding.Keyframes;

internal static class MatroskaKeyframes
{
    private const uint EbmlHeaderId = 0x1A45DFA3;
    private const uint SegmentId = 0x18538067;
    private const uint SeekHeadId = 0x114D9B74;
    private const uint SeekId = 0x4DBB;
    private const uint SeekIdId = 0x53AB;
    private const uint SeekPositionId = 0x53AC;
    private const uint InfoId = 0x1549A966;
    private const uint TimestampScaleId = 0x2AD7B1;
    private const uint TracksId = 0x1654AE6B;
    private const uint TrackEntryId = 0xAE;
    private const uint TrackNumberId = 0xD7;
    private const uint TrackTypeId = 0x83;
    private const uint CuesId = 0x1C53BB6B;
    private const uint CuePointId = 0xBB;
    private const uint CueTimeId = 0xB3;
    private const uint CueTrackPositionsId = 0xB7;
    private const uint CueTrackId = 0xF7;
    private const uint ClusterId = 0x1F43B675;

    private const ulong VideoTrackType = 1;
    private const ulong DefaultTimestampScale = 1_000_000;
    private const long LargestElement = 64L * 1024 * 1024;

    private sealed record Track(ulong Number, ulong Type);

    private sealed class Segment
    {
        public long DataStart { get; init; }
        public long DataEnd { get; init; }
        public ulong TimestampScale { get; set; } = DefaultTimestampScale;
        public bool HasInfo { get; set; }
        public List<Track>? Tracks { get; set; }
        public byte[]? Cues { get; set; }
        public Dictionary<uint, long> Positions { get; } = [];
    }

    public static bool Matches(ReadOnlySpan<byte> head) =>
        head.Length >= 4 && head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3;

    public static IReadOnlyList<double>? Read(Stream stream, int videoStreamIndex)
    {
        stream.Position = 0;
        var header = EbmlReader.ReadHeader(stream);
        if (header is not { Id: EbmlHeaderId, HasKnownSize: true }) return null;
        stream.Position += header.Value.Size;

        var segmentHeader = EbmlReader.ReadHeader(stream);
        if (segmentHeader?.Id != SegmentId) return null;
        long dataStart = stream.Position;
        var segment = new Segment
        {
            DataStart = dataStart,
            DataEnd = segmentHeader.Value.HasKnownSize ? Math.Min(stream.Length, dataStart + segmentHeader.Value.Size) : stream.Length
        };

        ScanUntilFirstCluster(stream, segment);
        FollowSeekEntries(stream, segment);

        if (segment.Tracks is null || segment.Cues is null) return null;
        var video = PickVideoTrack(segment.Tracks, videoStreamIndex);
        return video is null ? null : CueTimes(segment.Cues, video.Number, segment.TimestampScale);
    }

    private static void ScanUntilFirstCluster(Stream stream, Segment segment)
    {
        stream.Position = segment.DataStart;
        while (stream.Position < segment.DataEnd)
        {
            var element = EbmlReader.ReadHeader(stream);
            if (element is null || element.Value.Id == ClusterId || !element.Value.HasKnownSize) return;

            long bodyStart = stream.Position;
            Take(stream, segment, element.Value);
            stream.Position = bodyStart + element.Value.Size;
        }
    }

    private static void FollowSeekEntries(Stream stream, Segment segment)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var id in new[] { InfoId, TracksId, CuesId, SeekHeadId })
            {
                if (!segment.Positions.Remove(id, out var position)) continue;
                if (id == InfoId && segment.HasInfo) continue;
                if (id == TracksId && segment.Tracks is not null) continue;
                if (id == CuesId && segment.Cues is not null) continue;

                stream.Position = segment.DataStart + position;
                var element = EbmlReader.ReadHeader(stream);
                if (element is null || element.Value.Id != id || !element.Value.HasKnownSize) continue;
                Take(stream, segment, element.Value);
            }
        }
    }

    private static void Take(Stream stream, Segment segment, EbmlElement element)
    {
        switch (element.Id)
        {
            case SeekHeadId:
                ReadSeekHead(ReadBody(stream, element), segment.Positions);
                break;
            case InfoId:
                segment.TimestampScale = ReadTimestampScale(ReadBody(stream, element));
                segment.HasInfo = true;
                break;
            case TracksId:
                segment.Tracks = ReadTracks(ReadBody(stream, element));
                break;
            case CuesId:
                segment.Cues = ReadBody(stream, element);
                break;
        }
    }

    private static byte[] ReadBody(Stream stream, EbmlElement element)
    {
        if (element.Size > LargestElement) throw new InvalidDataException("Matroska element is too large to index.");
        var body = new byte[element.Size];
        stream.ReadExactly(body);
        return body;
    }

    private static void ReadSeekHead(ReadOnlySpan<byte> data, Dictionary<uint, long> positions)
    {
        foreach (var (id, start, size) in Children(data))
        {
            if (id != SeekId) continue;
            uint target = 0;
            long? position = null;
            foreach (var (childId, childStart, childSize) in Children(data.Slice(start, size)))
            {
                var value = data.Slice(start + childStart, childSize);
                if (childId == SeekIdId) target = (uint)EbmlReader.ReadUnsigned(value);
                else if (childId == SeekPositionId) position = (long)EbmlReader.ReadUnsigned(value);
            }
            if (target != 0 && position is { } found) positions.TryAdd(target, found);
        }
    }

    private static ulong ReadTimestampScale(ReadOnlySpan<byte> data)
    {
        foreach (var (id, start, size) in Children(data))
        {
            if (id == TimestampScaleId)
            {
                ulong scale = EbmlReader.ReadUnsigned(data.Slice(start, size));
                return scale > 0 ? scale : DefaultTimestampScale;
            }
        }
        return DefaultTimestampScale;
    }

    private static List<Track> ReadTracks(ReadOnlySpan<byte> data)
    {
        var tracks = new List<Track>();
        foreach (var (id, start, size) in Children(data))
        {
            if (id != TrackEntryId) continue;
            ulong number = 0;
            ulong type = 0;
            var entry = data.Slice(start, size);
            foreach (var (childId, childStart, childSize) in Children(entry))
            {
                if (childId == TrackNumberId) number = EbmlReader.ReadUnsigned(entry.Slice(childStart, childSize));
                else if (childId == TrackTypeId) type = EbmlReader.ReadUnsigned(entry.Slice(childStart, childSize));
            }
            tracks.Add(new Track(number, type));
        }
        return tracks;
    }

    private static Track? PickVideoTrack(List<Track> tracks, int videoStreamIndex)
    {
        if (videoStreamIndex >= 0 && videoStreamIndex < tracks.Count && tracks[videoStreamIndex].Type == VideoTrackType) return tracks[videoStreamIndex];
        return tracks.FirstOrDefault(track => track.Type == VideoTrackType);
    }

    private static List<double> CueTimes(ReadOnlySpan<byte> data, ulong videoTrack, ulong timestampScale)
    {
        var times = new SortedSet<double>();
        foreach (var (id, start, size) in Children(data))
        {
            if (id != CuePointId) continue;
            var point = data.Slice(start, size);
            ulong? time = null;
            bool forVideo = false;
            foreach (var (childId, childStart, childSize) in Children(point))
            {
                if (childId == CueTimeId)
                {
                    time = EbmlReader.ReadUnsigned(point.Slice(childStart, childSize));
                }
                else if (childId == CueTrackPositionsId)
                {
                    var positions = point.Slice(childStart, childSize);
                    foreach (var (positionId, positionStart, positionSize) in Children(positions))
                    {
                        if (positionId == CueTrackId && EbmlReader.ReadUnsigned(positions.Slice(positionStart, positionSize)) == videoTrack) forVideo = true;
                    }
                }
            }
            if (forVideo && time is { } ticks) times.Add(ticks * (double)timestampScale / 1_000_000_000d);
        }
        return [.. times];
    }

    private static List<(uint Id, int Start, int Size)> Children(ReadOnlySpan<byte> data)
    {
        var children = new List<(uint, int, int)>();
        int position = 0;
        while (position < data.Length)
        {
            var element = EbmlReader.ReadHeader(data, ref position);
            if (element is not { HasKnownSize: true } found || found.Size > data.Length - position) break;
            children.Add((found.Id, position, (int)found.Size));
            position += (int)found.Size;
        }
        return children;
    }
}
