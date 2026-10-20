namespace Vora.Application.Analysis;

public static class StreamBitrates
{
    public static long? Within(long? streamBitrate, long? fileBitrate) =>
        streamBitrate is > 0 && (fileBitrate is not > 0 || streamBitrate <= fileBitrate) ? streamBitrate : null;
}
