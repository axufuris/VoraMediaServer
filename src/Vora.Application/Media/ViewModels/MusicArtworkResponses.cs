namespace Vora.Application.Media.ViewModels;

// Anonymous types serialize correctly and then describe nothing: the OpenAPI
// document records a 200 with no schema, and the generated Swift and Kotlin
// clients hand back a response with no body to read.

public class ArtworkRefreshResponse
{
    // Separate from a null Url because they are different answers: nothing was
    // found, versus a provider was not asked because the slot was already filled
    // or locked.
    public bool Updated { get; set; }
    public string? ArtworkUrl { get; set; }
}

public class LastFmAuthCompleteResponse
{
    public string? Username { get; set; }
}
