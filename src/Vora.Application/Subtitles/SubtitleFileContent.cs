namespace Vora.Application.Subtitles;

public static class SubtitleFileContent
{
    private const int SampleBytes = 64 * 1024;

    public static bool HasText(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length == 0) return false;

            var buffer = new byte[(int)Math.Min(stream.Length, SampleBytes)];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            for (var i = 0; i < read; i++)
            {
                if (!IsBlank(buffer[i])) return true;
            }
            return stream.Length > read;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsBlank(byte value) =>
        value is 0x00 or 0x09 or 0x0A or 0x0D or 0x20 or 0xEF or 0xBB or 0xBF or 0xFE or 0xFF;
}
