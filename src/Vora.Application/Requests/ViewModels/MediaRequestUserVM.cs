namespace Vora.Application.Requests.ViewModels;

public class MediaRequestUserVM
{
    public Guid ProfileId { get; set; }
    public DateTime RequestedAt { get; set; }
    public UserProfileSimpleVM Profile { get; set; } = null!;
}
