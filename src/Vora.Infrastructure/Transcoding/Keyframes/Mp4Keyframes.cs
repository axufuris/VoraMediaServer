using System.Buffers.Binary;
using System.Text;

namespace Vora.Infrastructure.Transcoding.Keyframes;

internal static class Mp4Keyframes
{
    private const long LargestMovieBox = 256L * 1024 * 1024;
    private static readonly string[] TopLevelKinds = ["ftyp", "moov", "mdat", "free", "wide", "skip", "pnot"];

    private sealed class TrackBoxes
    {
        public string Handler { get; set; } = string.Empty;
        public uint Timescale { get; set; }
        public long EmptyEditMovieUnits { get; set; }
        public long MediaTime { get; set; }
        public List<(uint Count, uint Delta)> TimeToSample { get; } = [];
        public List<(uint Count, int Offset)> CompositionOffsets { get; } = [];
        public List<uint>? SyncSamples { get; set; }
    }

    public static bool Matches(ReadOnlySpan<byte> head) =>
        head.Length >= 8 && TopLevelKinds.Contains(Encoding.ASCII.GetString(head.Slice(4, 4)));

    public static IReadOnlyList<double>? Read(Stream stream, int videoStreamIndex)
    {
        var movie = ReadMovieBox(stream);
        if (movie is null) return null;

        uint movieTimescale = 0;
        var tracks = new List<TrackBoxes>();
        foreach (var (kind, body) in Boxes(movie))
        {
            if (kind == "mvhd") movieTimescale = MovieTimescale(body);
            else if (kind == "trak") tracks.Add(ReadTrack(body));
        }

        var video = videoStreamIndex >= 0 && videoStreamIndex < tracks.Count && tracks[videoStreamIndex].Handler == "vide"
            ? tracks[videoStreamIndex]
            : tracks.FirstOrDefault(track => track.Handler == "vide");
        if (video is null || video.Timescale == 0 || video.TimeToSample.Count == 0) return null;

        return KeyframeTimes(video, movieTimescale);
    }

    private static byte[]? ReadMovieBox(Stream stream)
    {
        long position = 0;
        Span<byte> header = stackalloc byte[16];
        while (position + 8 <= stream.Length)
        {
            stream.Position = position;
            stream.ReadExactly(header[..8]);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string kind = Encoding.ASCII.GetString(header.Slice(4, 4));
            int headerLength = 8;
            if (size == 1)
            {
                stream.ReadExactly(header.Slice(8, 8));
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.Slice(8, 8));
                headerLength = 16;
            }
            else if (size == 0)
            {
                size = stream.Length - position;
            }
            if (size < headerLength) return null;

            if (kind == "moov")
            {
                long bodySize = size - headerLength;
                if (bodySize > LargestMovieBox) throw new InvalidDataException("MP4 movie box is too large to index.");
                var body = new byte[bodySize];
                stream.ReadExactly(body);
                return body;
            }
            position += size;
        }
        return null;
    }

    private static TrackBoxes ReadTrack(ReadOnlySpan<byte> trak)
    {
        var track = new TrackBoxes();
        foreach (var (kind, body) in Boxes(trak))
        {
            if (kind == "edts") ReadEdits(body, track);
            else if (kind == "mdia") ReadMedia(body, track);
        }
        return track;
    }

    private static void ReadEdits(ReadOnlySpan<byte> edts, TrackBoxes track)
    {
        foreach (var (kind, body) in Boxes(edts))
        {
            if (kind != "elst" || body.Length < 8) continue;
            int version = body[0];
            uint count = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
            int position = 8;
            for (uint i = 0; i < count; i++)
            {
                long duration;
                long mediaTime;
                if (version == 1)
                {
                    duration = (long)BinaryPrimitives.ReadUInt64BigEndian(body[position..]);
                    mediaTime = BinaryPrimitives.ReadInt64BigEndian(body[(position + 8)..]);
                    position += 20;
                }
                else
                {
                    duration = BinaryPrimitives.ReadUInt32BigEndian(body[position..]);
                    mediaTime = BinaryPrimitives.ReadInt32BigEndian(body[(position + 4)..]);
                    position += 12;
                }

                if (mediaTime == -1)
                {
                    track.EmptyEditMovieUnits += duration;
                    continue;
                }
                track.MediaTime = mediaTime;
                return;
            }
        }
    }

    private static void ReadMedia(ReadOnlySpan<byte> mdia, TrackBoxes track)
    {
        foreach (var (kind, body) in Boxes(mdia))
        {
            if (kind == "mdhd") track.Timescale = MediaTimescale(body);
            else if (kind == "hdlr" && body.Length >= 12) track.Handler = Encoding.ASCII.GetString(body.AsSpan(8, 4));
            else if (kind == "minf") ReadMediaInformation(body, track);
        }
    }

    private static void ReadMediaInformation(ReadOnlySpan<byte> minf, TrackBoxes track)
    {
        foreach (var (kind, body) in Boxes(minf))
        {
            if (kind != "stbl") continue;
            foreach (var (tableKind, table) in Boxes(body))
            {
                switch (tableKind)
                {
                    case "stts":
                        foreach (var (count, value) in Entries(table)) track.TimeToSample.Add((count, value));
                        break;
                    case "ctts":
                        foreach (var (count, value) in Entries(table)) track.CompositionOffsets.Add((count, unchecked((int)value)));
                        break;
                    case "stss":
                        track.SyncSamples = SyncSamples(table);
                        break;
                }
            }
        }
    }

    private static IEnumerable<(uint Count, uint Value)> Entries(ReadOnlySpan<byte> table)
    {
        var entries = new List<(uint, uint)>();
        if (table.Length < 8) return entries;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(table[4..]);
        for (int i = 0; i < count && 8 + (i + 1) * 8 <= table.Length; i++)
        {
            entries.Add((BinaryPrimitives.ReadUInt32BigEndian(table[(8 + i * 8)..]), BinaryPrimitives.ReadUInt32BigEndian(table[(12 + i * 8)..])));
        }
        return entries;
    }

    private static List<uint> SyncSamples(ReadOnlySpan<byte> table)
    {
        var samples = new List<uint>();
        if (table.Length < 8) return samples;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(table[4..]);
        for (int i = 0; i < count && 8 + (i + 1) * 4 <= table.Length; i++) samples.Add(BinaryPrimitives.ReadUInt32BigEndian(table[(8 + i * 4)..]));
        return samples;
    }

    private static uint MovieTimescale(ReadOnlySpan<byte> mvhd) => Timescale(mvhd);

    private static uint MediaTimescale(ReadOnlySpan<byte> mdhd) => Timescale(mdhd);

    private static uint Timescale(ReadOnlySpan<byte> header)
    {
        int offset = header.Length > 0 && header[0] == 1 ? 20 : 12;
        return header.Length < offset + 4 ? 0 : BinaryPrimitives.ReadUInt32BigEndian(header[offset..]);
    }

    private static List<double> KeyframeTimes(TrackBoxes track, uint movieTimescale)
    {
        double emptyEdit = movieTimescale > 0 ? track.EmptyEditMovieUnits / (double)movieTimescale : 0;
        var sync = track.SyncSamples is null ? null : new HashSet<uint>(track.SyncSamples);
        var times = new SortedSet<double>();

        long decodeTime = 0;
        uint sample = 1;
        int offsetRun = 0;
        uint offsetLeft = track.CompositionOffsets.Count > 0 ? track.CompositionOffsets[0].Count : 0;

        foreach (var (count, delta) in track.TimeToSample)
        {
            for (uint i = 0; i < count; i++, sample++)
            {
                int composition = 0;
                if (offsetRun < track.CompositionOffsets.Count)
                {
                    composition = track.CompositionOffsets[offsetRun].Offset;
                    if (--offsetLeft == 0 && ++offsetRun < track.CompositionOffsets.Count) offsetLeft = track.CompositionOffsets[offsetRun].Count;
                }

                if (sync is null || sync.Contains(sample))
                {
                    times.Add((decodeTime + composition - track.MediaTime) / (double)track.Timescale + emptyEdit);
                }
                decodeTime += delta;
            }
        }
        return [.. times];
    }

    private static List<(string Kind, byte[] Body)> Boxes(ReadOnlySpan<byte> data)
    {
        var boxes = new List<(string, byte[])>();
        int position = 0;
        while (position + 8 <= data.Length)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            string kind = Encoding.ASCII.GetString(data.Slice(position + 4, 4));
            int headerLength = 8;
            if (size == 1)
            {
                if (position + 16 > data.Length) break;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data[(position + 8)..]);
                headerLength = 16;
            }
            else if (size == 0)
            {
                size = data.Length - position;
            }
            if (size < headerLength || position + size > data.Length) break;
            boxes.Add((kind, data.Slice(position + headerLength, (int)size - headerLength).ToArray()));
            position += (int)size;
        }
        return boxes;
    }
}
