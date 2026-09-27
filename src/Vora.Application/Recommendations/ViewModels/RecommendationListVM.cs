using Vora.Application.Libraries.ViewModels;

namespace Vora.Application.Recommendations.ViewModels;

public class RecommendationListVM
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Weight { get; set; }
    public List<LibraryItemVM> Items { get; set; } = new();
}
