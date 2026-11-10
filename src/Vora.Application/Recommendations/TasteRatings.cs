namespace Vora.Application.Recommendations;

// Ratings are out of 10. What counts as loving or disliking a title for the
// For You rows, the same for the local rows and the AI's prompt.
public static class TasteRatings
{
    public const decimal Loved = 8m;
    public const decimal Disliked = 4m;
}
