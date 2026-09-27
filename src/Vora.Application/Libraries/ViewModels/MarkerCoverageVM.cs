namespace Vora.Application.Libraries.ViewModels;

public class MarkerCoverageVM
{
    public Guid LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int ItemsWithAnyMarker { get; set; }
    public int ItemsWithIntro { get; set; }
    public int ItemsWithCredits { get; set; }
    public int ItemsWithCreditsScene { get; set; }
    public int ItemsWithRecap { get; set; }
    public int ItemsWithPreview { get; set; }
    public int ItemsMissingDuration { get; set; }
}
