namespace Vora.Application.Iptv.ViewModels;

public class HdHomeRunStatusVM
{
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public string? DeviceName { get; set; }
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? DeviceId { get; set; }
    public int TunerCount { get; set; }
    public int TunersInUse { get; set; }
    public int VoraStreams { get; set; }
    public List<HdHomeRunTunerVM> Tuners { get; set; } = new();
}

public class HdHomeRunTunerVM
{
    public int Index { get; set; }
    public bool InUse { get; set; }
    public string? ChannelNumber { get; set; }
    public string? ChannelName { get; set; }
    public double? FrequencyMhz { get; set; }
    public int? SignalStrengthPercent { get; set; }
    public int? SignalQualityPercent { get; set; }
    public int? SymbolQualityPercent { get; set; }
    public long? BitsPerSecond { get; set; }
    public string? TargetAddress { get; set; }
}
