using System.Linq.Expressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Ai.Dtos;

public class MediaItemForEmbeddingDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Overview { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Cast { get; set; } = new();

    public static Expression<Func<MediaItem, MediaItemForEmbeddingDto>> Projection =>
        m => new MediaItemForEmbeddingDto
        {
            Id = m.Id,
            Title = m.Title,
            Overview = m.Overview,
            Genres = m.Genres.Select(g => g.Name).ToList(),
            Cast = m.Cast.OrderBy(c => c.Order).Take(5).Select(c => c.Actor!.Name).ToList()
        };
}
