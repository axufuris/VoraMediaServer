namespace Vora.Application.Media.Ai;

public class MakeAiPlaylistRequest
{
    // What the playlist is for, in the listener's words. Up to 300 characters.
    public string Prompt { get; set; } = string.Empty;
}

public class CreateBlendRequest
{
    public Guid PartnerProfileId { get; set; }
}

public class AiPlaylistCreatedResponse
{
    // A mix: open it with GET /api/music/recommendations/mixes/{mixId}, keep it
    // with POST .../mixes/{mixId}/save.
    public Guid MixId { get; set; }
}
