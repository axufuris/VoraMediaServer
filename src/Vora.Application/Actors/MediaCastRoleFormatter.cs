using Vora.Domain.Enums;

namespace Vora.Application.Actors;

internal static class MediaCastRoleFormatter
{
    public static string Format(MediaCastRole roles)
    {
        if (roles == MediaCastRole.None) return string.Empty;

        var labels = new List<string>(5);
        if (roles.HasFlag(MediaCastRole.Actor)) labels.Add("Actor");
        if (roles.HasFlag(MediaCastRole.Director)) labels.Add("Director");
        if (roles.HasFlag(MediaCastRole.Writer)) labels.Add("Writer");
        if (roles.HasFlag(MediaCastRole.Producer)) labels.Add("Producer");
        if (roles.HasFlag(MediaCastRole.Creator)) labels.Add("Creator");
        return string.Join(", ", labels);
    }
}
