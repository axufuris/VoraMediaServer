namespace Vora.Application.Admin.ViewModels;

public class ActiveStreamVM
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public Guid MediaItemId { get; set; }
    public string MediaTitle { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty;
    public string PlaybackState { get; set; } = string.Empty;
    public bool IsTranscoding { get; set; }
    public TimeSpan CurrentPosition { get; set; }
}
