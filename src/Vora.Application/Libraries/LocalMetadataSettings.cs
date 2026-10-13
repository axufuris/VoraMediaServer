using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;

namespace Vora.Application.Libraries;

public static class LocalMetadataSettings
{
    public static LocalMetadataMode Mode(int requested, LibraryType type) =>
        type.ReadsLocalMetadata() && Enum.IsDefined(typeof(LocalMetadataMode), requested)
            ? (LocalMetadataMode)requested
            : LocalMetadataMode.Off;

    public static bool PreferArtwork(bool requested, LibraryType type) =>
        requested && type.ReadsLocalMetadata();
}
