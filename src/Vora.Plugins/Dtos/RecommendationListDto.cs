namespace Vora.Plugins.Dtos;

public class RecommendationListDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int Weight { get; set; }

    public List<Guid> LocalItemIds { get; set; } = new();
    public List<string> ExternalTmdbIds { get; set; } = new();
}
