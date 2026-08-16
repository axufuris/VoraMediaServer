namespace Vora.Application.Media.Dtos;

public class MarkerDetectionGateDto
{
    public List<string>? LockedFields { get; set; }
    public DateTime? MarkersAnalyzedAt { get; set; }
    public bool EnableIntroDetection { get; set; }
    public bool EnableCreditsDetection { get; set; }

    public bool AreMarkersLocked =>
        LockedFields != null && LockedFields.Contains("Markers", StringComparer.OrdinalIgnoreCase);
}
