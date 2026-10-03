using Vora.Domain.Enums;

namespace Vora.Application.Media.ViewModels;

public class TrackInfoVM
{
    public Guid Id { get; set; }
    public AudioQualityVM? Quality { get; set; }
    public List<string> Moods { get; set; } = new();
    public TrackEnergy? Energy { get; set; }
    public List<string> Themes { get; set; } = new();
    public List<string> GoodFor { get; set; } = new();
    public bool IsInstrumental { get; set; }
}
