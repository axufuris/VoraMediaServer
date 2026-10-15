using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;

namespace Vora.Application.Media.Dtos;

public class MarkerDetectionGateDto
{
    public List<string>? LockedFields { get; set; }
    public DateTime? MarkersAnalyzedAt { get; set; }
    public bool EnableIntroDetection { get; set; }
    public bool EnableCreditsDetection { get; set; }
    public LibraryType LibraryType { get; set; }

    public bool DetectsIntro => EnableIntroDetection && LibraryType.DetectsMarkers();
    public bool DetectsCredits => EnableCreditsDetection && LibraryType.DetectsMarkers();

    public bool AreMarkersLocked =>
        LockedFields != null && LockedFields.Contains("Markers", StringComparer.OrdinalIgnoreCase);
}
