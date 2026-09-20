using System.Linq.Expressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Media.ViewModels;

public class MediaMarkerVM
{
    public string Type { get; set; } = string.Empty;
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public int Order { get; set; }

    public static Expression<Func<MediaItemMarker, MediaMarkerVM>> Projection =>
        m => new MediaMarkerVM
        {
            Type = m.Type.ToString(),
            StartSeconds = m.Start.TotalSeconds,
            EndSeconds = m.End.TotalSeconds,
            Order = m.Order
        };
}
