namespace Vora.Plugins.Dtos;

public class RemoteUserDataDto
{
    public required IReadOnlyList<RemoteWatchStateDto> WatchStates { get; set; }
    public required IReadOnlyList<RemoteRatingDto> Ratings { get; set; }
}
