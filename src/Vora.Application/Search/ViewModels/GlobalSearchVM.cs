namespace Vora.Application.Search.ViewModels;

public class GlobalSearchVM
{
    public string Query { get; set; } = string.Empty;
    public IEnumerable<MediaSearchResultVM> Movies { get; set; } = new List<MediaSearchResultVM>();
    public IEnumerable<MediaSearchResultVM> TvShows { get; set; } = new List<MediaSearchResultVM>();
    public IEnumerable<ActorSearchResultVM> Actors { get; set; } = new List<ActorSearchResultVM>();
    public IEnumerable<CollectionSearchResultVM> Collections { get; set; } = new List<CollectionSearchResultVM>();
    public IEnumerable<MusicSearchResultVM> Music { get; set; } = new List<MusicSearchResultVM>();
}
