namespace Vora.Infrastructure.Transcoding.Keyframes;

internal readonly record struct EbmlElement(uint Id, long Size)
{
    public bool HasKnownSize => Size >= 0;
}

internal static class EbmlReader
{
    public const long UnknownSize = -1;

    public static EbmlElement? ReadHeader(Stream stream)
    {
        int first = stream.ReadByte();
        if (first <= 0) return null;
        int idLength = LeadingZeros((byte)first) + 1;
        if (idLength > 4) return null;

        uint id = (uint)first;
        for (int i = 1; i < idLength; i++)
        {
            int next = stream.ReadByte();
            if (next < 0) return null;
            id = (id << 8) | (uint)next;
        }

        int sizeFirst = stream.ReadByte();
        if (sizeFirst <= 0) return null;
        int sizeLength = LeadingZeros((byte)sizeFirst) + 1;
        if (sizeLength > 8) return null;

        long size = sizeFirst & (0xFF >> sizeLength);
        bool allOnes = size == (0xFF >> sizeLength);
        for (int i = 1; i < sizeLength; i++)
        {
            int next = stream.ReadByte();
            if (next < 0) return null;
            size = (size << 8) | (uint)next;
            allOnes &= next == 0xFF;
        }

        return new EbmlElement(id, allOnes ? UnknownSize : size);
    }

    public static EbmlElement? ReadHeader(ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length) return null;
        int first = data[position];
        if (first == 0) return null;
        int idLength = LeadingZeros((byte)first) + 1;
        if (idLength > 4 || position + idLength > data.Length) return null;

        uint id = 0;
        for (int i = 0; i < idLength; i++) id = (id << 8) | data[position + i];
        position += idLength;

        if (position >= data.Length) return null;
        int sizeFirst = data[position];
        if (sizeFirst == 0) return null;
        int sizeLength = LeadingZeros((byte)sizeFirst) + 1;
        if (sizeLength > 8 || position + sizeLength > data.Length) return null;

        long size = sizeFirst & (0xFF >> sizeLength);
        bool allOnes = size == (0xFF >> sizeLength);
        for (int i = 1; i < sizeLength; i++)
        {
            size = (size << 8) | data[position + i];
            allOnes &= data[position + i] == 0xFF;
        }
        position += sizeLength;

        return new EbmlElement(id, allOnes ? UnknownSize : size);
    }

    public static ulong ReadUnsigned(ReadOnlySpan<byte> data)
    {
        ulong value = 0;
        foreach (byte part in data) value = (value << 8) | part;
        return value;
    }

    private static int LeadingZeros(byte value)
    {
        int count = 0;
        for (int mask = 0x80; mask > 0 && (value & mask) == 0; mask >>= 1) count++;
        return count;
    }
}
