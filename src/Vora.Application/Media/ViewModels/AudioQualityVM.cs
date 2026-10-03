namespace Vora.Application.Media.ViewModels;

public class AudioQualityVM
{
    public string Format { get; set; } = string.Empty;
    public int? SampleRate { get; set; }
    public int? Bitrate { get; set; }
    public bool Lossless { get; set; }
    public bool HiRes { get; set; }
    public string Label { get; set; } = string.Empty;
}
