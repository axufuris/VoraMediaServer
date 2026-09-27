namespace Vora.Application.Media.Requests;

public class SetAlbumContentRatingRequest
{
    // "Explicit", "Clean", or null / empty for none.
    public string? ContentRating { get; set; }
}
