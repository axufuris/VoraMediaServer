namespace Vora.Application.Podcasts.ViewModels;

// Where an episode's audio redirect chain ends (GET /api/podcasts/episodes/{id}/audio).
public class PodcastEpisodeAudioVM
{
    public string Url { get; set; } = string.Empty;
}
