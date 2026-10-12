using System.Buffers.Binary;

namespace Vora.Infrastructure.Tests.Transcoding;

internal static class PgsSubtitleFile
{
    private const byte PaletteSegment = 0x14;
    private const byte ObjectSegment = 0x15;
    private const byte CompositionSegment = 0x16;
    private const byte WindowSegment = 0x17;
    private const byte EndSegment = 0x80;

    public static byte[] WhiteBox(int canvasWidth, int canvasHeight, int x, int y, int width, int height, TimeSpan showAt, TimeSpan hideAt, bool startEmpty)
    {
        var file = new MemoryStream();
        var number = 0;
        if (startEmpty) Clear(file, TimeSpan.Zero, canvasWidth, canvasHeight, x, y, width, height, number++);
        Show(file, showAt, canvasWidth, canvasHeight, x, y, width, height, number++);
        Clear(file, hideAt, canvasWidth, canvasHeight, x, y, width, height, number);
        return file.ToArray();
    }

    private static void Show(MemoryStream file, TimeSpan at, int canvasWidth, int canvasHeight, int x, int y, int width, int height, int number)
    {
        var pts = Pts(at);
        Write(file, CompositionSegment, pts, [.. Composition(canvasWidth, canvasHeight, number, epochStart: true, objects: 1), .. U16(0), 0, 0, .. U16(x), .. U16(y)]);
        Write(file, WindowSegment, pts, Window(x, y, width, height));
        Write(file, PaletteSegment, pts, [0, 0, 1, 235, 128, 128, 255]);
        var bitmap = FilledRows(width, height);
        var length = bitmap.Length + 4;
        Write(file, ObjectSegment, pts, [.. U16(0), 0, 0xC0, (byte)(length >> 16), (byte)(length >> 8), (byte)length, .. U16(width), .. U16(height), .. bitmap]);
        Write(file, EndSegment, pts, []);
    }

    private static void Clear(MemoryStream file, TimeSpan at, int canvasWidth, int canvasHeight, int x, int y, int width, int height, int number)
    {
        var pts = Pts(at);
        Write(file, CompositionSegment, pts, Composition(canvasWidth, canvasHeight, number, epochStart: false, objects: 0));
        Write(file, WindowSegment, pts, Window(x, y, width, height));
        Write(file, EndSegment, pts, []);
    }

    private static byte[] Composition(int canvasWidth, int canvasHeight, int number, bool epochStart, byte objects) =>
        [.. U16(canvasWidth), .. U16(canvasHeight), 0x10, .. U16(number), (byte)(epochStart ? 0x80 : 0x00), 0, 0, objects];

    private static byte[] Window(int x, int y, int width, int height) =>
        [1, 0, .. U16(x), .. U16(y), .. U16(width), .. U16(height)];

    private static byte[] FilledRows(int width, int height)
    {
        byte[] run = width < 64 ? [0, (byte)(0x80 | width), 1] : [0, (byte)(0xC0 | (width >> 8)), (byte)width, 1];
        var rows = new List<byte>();
        for (var row = 0; row < height; row++)
        {
            rows.AddRange(run);
            rows.AddRange([0, 0]);
        }
        return [.. rows];
    }

    private static void Write(MemoryStream file, byte type, uint pts, byte[] data)
    {
        var header = new byte[13];
        header[0] = (byte)'P';
        header[1] = (byte)'G';
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(2), pts);
        header[10] = type;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(11), (ushort)data.Length);
        file.Write(header);
        file.Write(data);
    }

    private static uint Pts(TimeSpan at) => (uint)(at.TotalSeconds * 90000);

    private static byte[] U16(int value) => [(byte)(value >> 8), (byte)value];
}
