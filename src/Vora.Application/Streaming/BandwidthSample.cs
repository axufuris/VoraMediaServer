namespace Vora.Application.Streaming;

public static class BandwidthSample
{
    public const int MinKilobytes = 64;
    public const int MaxKilobytes = 16 * 1024;
    private const int BlockBytes = 64 * 1024;

    private static readonly byte[] Block = RandomBlock();

    public static int ClampKilobytes(int kilobytes) => Math.Clamp(kilobytes, MinKilobytes, MaxKilobytes);

    public static async Task WriteAsync(Stream output, int kilobytes, CancellationToken cancellationToken)
    {
        long remaining = ClampKilobytes(kilobytes) * 1024L;
        while (remaining > 0)
        {
            var count = (int)Math.Min(remaining, Block.Length);
            await output.WriteAsync(Block.AsMemory(0, count), cancellationToken);
            remaining -= count;
        }
    }

    private static byte[] RandomBlock()
    {
        var block = new byte[BlockBytes];
        Random.Shared.NextBytes(block);
        return block;
    }
}
