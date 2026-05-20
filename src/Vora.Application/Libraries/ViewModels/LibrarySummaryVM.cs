using System.Linq.Expressions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;

namespace Vora.Application.Libraries.ViewModels;

public class LibrarySummaryVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsBeingWatched { get; set; }

    public static Expression<Func<MediaLibrary, LibrarySummaryVM>> Projection =>
        l => new LibrarySummaryVM
        {
            Id = l.Id,
            Name = l.Name,
            Type = l.Type == LibraryType.Movie ? "Movie"
                : l.Type == LibraryType.TvShow ? "TvShow"
                : l.Type == LibraryType.Music ? "Music"
                : l.Type == LibraryType.HomeVideo ? "HomeVideo"
                : l.Type == LibraryType.LiveTv ? "LiveTv"
                : "Unknown"
        };
}
