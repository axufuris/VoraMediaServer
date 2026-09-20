namespace Vora.Application.SmartLists.Dtos;

public class SmartListRulesDto
{
    public List<int>? GenreIds { get; set; }
    public int? Decade { get; set; }
    public bool? UnwatchedOnly { get; set; }
    public List<string>? MediaTypes { get; set; }
    public string? ContentRating { get; set; }
}
