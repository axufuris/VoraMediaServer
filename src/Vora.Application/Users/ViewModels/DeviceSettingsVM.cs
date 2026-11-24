namespace Vora.Application.Users.ViewModels;

public class DeviceSettingsVM
{
    public int Bitrate { get; set; }
    public int MaxResolution { get; set; }
    public int SeekStepSeconds { get; set; }
    public int VolumeStepPercent { get; set; }
    public bool SoundOnClick { get; set; }
    public bool SoundOnNavOpen { get; set; }
    public bool SoundOnMove { get; set; }
}
