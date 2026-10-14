using System.Buffers.Binary;
using System.Text;
using Vora.Infrastructure.Transcoding.Keyframes;

namespace Vora.Infrastructure.Tests.Transcoding;

public class KeyframeReaderTests
{
    private static byte[] Ebml(uint id, params byte[][] children) => Ebml(id, children.SelectMany(child => child).ToArray());

    private static byte[] Ebml(uint id, byte[] body)
    {
        var idBytes = new List<byte>();
        for (uint value = id; value > 0; value >>= 8) idBytes.Insert(0, (byte)value);
        var size = new byte[8];
        size[0] = 0x01;
        BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)body.Length);
        size[0] = 0x01;
        return [.. idBytes, .. size, .. body];
    }

    private static byte[] Unsigned(uint id, ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return Ebml(id, bytes);
    }

    private static byte[] Matroska(byte[][] beforeClusters, byte[][] afterClusters, bool seekHeadPointsAtCues)
    {
        var header = Ebml(0x1A45DFA3, Unsigned(0x4286, 1));
        var cluster = Ebml(0x1F43B675, Unsigned(0xE7, 0), new byte[64]);
        var front = beforeClusters.SelectMany(part => part).ToArray();

        byte[] seekHead = [];
        if (seekHeadPointsAtCues)
        {
            var placeholder = SeekHead(0);
            long cuesPosition = placeholder.Length + front.Length + cluster.Length;
            seekHead = SeekHead(cuesPosition);
        }

        var body = seekHead.Concat(front).Concat(cluster).Concat(afterClusters.SelectMany(part => part)).ToArray();
        return [.. header, .. Ebml(0x18538067, body)];
    }

    private static byte[] SeekHead(long cuesPosition) =>
        Ebml(0x114D9B74, Ebml(0x4DBB, Ebml(0x53AB, [0x1C, 0x53, 0xBB, 0x6B]), Unsigned(0x53AC, (ulong)cuesPosition)));

    private static byte[] Info(ulong timestampScale) => Ebml(0x1549A966, Unsigned(0x2AD7B1, timestampScale));

    private static byte[] Tracks(params (ulong Number, ulong Type)[] tracks) =>
        Ebml(0x1654AE6B, tracks.Select(track => Ebml(0xAE, Unsigned(0xD7, track.Number), Unsigned(0x83, track.Type))).ToArray());

    private static byte[] Cues(params (ulong Time, ulong Track)[] points) =>
        Ebml(0x1C53BB6B, points.Select(point => Ebml(0xBB, Unsigned(0xB3, point.Time), Ebml(0xB7, Unsigned(0xF7, point.Track), Unsigned(0xF1, 0)))).ToArray());

    private static IReadOnlyList<double>? ReadMatroska(byte[] file, int videoStreamIndex = 0)
    {
        using var stream = new MemoryStream(file);
        MatroskaKeyframes.Matches(file).Should().BeTrue();
        return MatroskaKeyframes.Read(stream, videoStreamIndex);
    }

    [Fact]
    public void Matroska_keyframes_come_from_the_video_track_cues_at_the_end_of_the_file()
    {
        var file = Matroska(
            [Info(1_000_000), Tracks((1, 2), (2, 1))],
            [Cues((0, 2), (0, 1), (4004, 2), (5000, 1), (8008, 2))],
            seekHeadPointsAtCues: true);

        ReadMatroska(file).Should().Equal(0, 4.004, 8.008);
    }

    [Fact]
    public void Matroska_cues_written_before_the_clusters_are_read_without_a_seek_entry()
    {
        var file = Matroska([Info(1_000_000), Tracks((1, 1)), Cues((0, 1), (2002, 1))], [], seekHeadPointsAtCues: false);

        ReadMatroska(file).Should().Equal(0, 2.002);
    }

    [Fact]
    public void Matroska_cue_times_follow_the_timestamp_scale()
    {
        var file = Matroska([Info(100_000), Tracks((1, 1)), Cues((0, 1), (40_040, 1))], [], seekHeadPointsAtCues: false);

        ReadMatroska(file).Should().Equal(0, 4.004);
    }

    [Fact]
    public void Matroska_picks_the_chosen_video_stream_when_there_are_two()
    {
        var file = Matroska([Info(1_000_000), Tracks((1, 1), (2, 1)), Cues((0, 1), (3000, 1), (0, 2), (6000, 2))], [], seekHeadPointsAtCues: false);

        ReadMatroska(file, videoStreamIndex: 1).Should().Equal(0, 6);
    }

    [Fact]
    public void A_matroska_file_without_cues_has_no_keyframe_index()
    {
        var file = Matroska([Info(1_000_000), Tracks((1, 1))], [], seekHeadPointsAtCues: false);

        ReadMatroska(file).Should().BeNull();
    }

    private static byte[] Box(string kind, params byte[][] parts)
    {
        var body = parts.SelectMany(part => part).ToArray();
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(body.Length + 8));
        Encoding.ASCII.GetBytes(kind).CopyTo(header, 4);
        return [.. header, .. body];
    }

    private static byte[] Words(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    private static byte[] FullBox(string kind, params uint[] words) => Box(kind, Words([0, .. words]));

    private static byte[] TimescaleBox(string kind, uint timescale) => FullBox(kind, 0, 0, timescale, 0);

    private static byte[] Trak(string handler, uint timescale, uint[] stts, uint[]? ctts, uint[]? stss, uint[]? elst) =>
        Box("trak",
            elst is null ? [] : Box("edts", FullBox("elst", elst)),
            Box("mdia",
                TimescaleBox("mdhd", timescale),
                Box("hdlr", Words(0, 0), Encoding.ASCII.GetBytes(handler), new byte[12]),
                Box("minf", Box("stbl",
                    FullBox("stts", stts),
                    ctts is null ? [] : FullBox("ctts", ctts),
                    stss is null ? [] : FullBox("stss", stss)))));

    private static IReadOnlyList<double>? ReadMp4(byte[] moov)
    {
        var file = Box("ftyp", Encoding.ASCII.GetBytes("isom"), Words(0)).Concat(Box("mdat", new byte[32])).Concat(moov).ToArray();
        Mp4Keyframes.Matches(file).Should().BeTrue();
        using var stream = new MemoryStream(file);
        return Mp4Keyframes.Read(stream, 1);
    }

    [Fact]
    public void Mp4_keyframes_are_presentation_times_of_the_sync_samples_after_the_edit_list()
    {
        var moov = Box("moov",
            TimescaleBox("mvhd", 1000),
            Trak("soun", 48000, [1, 1, 1024], null, null, null),
            Trak("vide", 24000, [1, 6, 1000], [1, 6, 2000], [2, 1, 4], [1, 6000, 2000, 1 << 16]));

        ReadMp4(moov).Should().Equal(0, 0.125);
    }

    [Fact]
    public void An_empty_edit_moves_mp4_keyframes_later()
    {
        var moov = Box("moov",
            TimescaleBox("mvhd", 1000),
            Trak("soun", 48000, [1, 1, 1024], null, null, null),
            Trak("vide", 1000, [1, 3, 500], null, [2, 1, 3], [2, 500, uint.MaxValue, 1 << 16, 1500, 0, 1 << 16]));

        ReadMp4(moov).Should().Equal(0.5, 1.5);
    }

    [Fact]
    public void An_mp4_without_a_sync_sample_table_has_every_sample_as_a_keyframe()
    {
        var moov = Box("moov", TimescaleBox("mvhd", 1000), Trak("soun", 48000, [1, 1, 1024], null, null, null), Trak("vide", 10, [1, 3, 5], null, null, null));

        ReadMp4(moov).Should().Equal(0, 0.5, 1.0);
    }

    [Fact]
    public void An_mp4_whose_samples_are_in_fragments_has_no_keyframe_index()
    {
        var moov = Box("moov", TimescaleBox("mvhd", 1000), Trak("soun", 48000, [1, 1, 1024], null, null, null), Trak("vide", 1000, [], null, null, null));

        ReadMp4(moov).Should().BeNull();
    }

    [Fact]
    public void A_file_that_is_neither_matroska_nor_mp4_has_no_keyframe_index()
    {
        var path = Path.Combine(Path.GetTempPath(), "vora-keyframes-" + Guid.NewGuid().ToString("N") + ".avi");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("RIFF....AVI LIST"));
        try
        {
            KeyframeIndex.TryRead(path, 0).Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_damaged_file_has_no_keyframe_index_instead_of_failing_playback()
    {
        var path = Path.Combine(Path.GetTempPath(), "vora-keyframes-" + Guid.NewGuid().ToString("N") + ".mkv");
        File.WriteAllBytes(path, [0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x18]);
        try
        {
            KeyframeIndex.TryRead(path, 0).Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
