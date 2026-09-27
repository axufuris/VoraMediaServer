namespace Vora.Plugins.Dtos;

public record ScanFileResult(Guid? MediaItemId, Guid? ParentShowId, bool NewSeasonCreated)
{
    public static ScanFileResult None => new(null, null, false);
}
