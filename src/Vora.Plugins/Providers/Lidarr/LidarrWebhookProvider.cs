using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.Arr;

namespace Vora.Plugins.Providers.Lidarr;

public class LidarrWebhookProvider : ArrWebhookProviderBase
{
    public LidarrWebhookProvider() : base("Lidarr")
    {
    }

    public override string Id => "lidarr_webhook";
    public override string Name => "Lidarr Webhook";
    public override string Description => "Lets Lidarr tell Vora when a track file is imported, upgraded, renamed, retagged or deleted, so the music library updates at once. Add a Webhook connection in Lidarr (Settings, Connect) pointing at Vora.";
    protected override LibraryKind Kind => LibraryKind.Music;

    public override MediaWebhookParseResult Parse(string body)
    {
        using var document = Open(body);
        var root = document.RootElement;
        var eventName = Text(root, "eventType") ?? string.Empty;
        var instance = Text(root, "instanceName");

        switch (eventName)
        {
            case "Test":
                return MediaWebhookParseResult.Test(instance);

            case "Download":
            {
                var added = Paths(Items(root, "trackFiles")).Distinct().ToList();
                var deleted = Paths(Items(root, "deletedFiles"));
                return MediaWebhookParseResult.Of(eventName, instance, Additions(added).Concat(Removals(deleted, added)));
            }

            case "Rename":
                return MediaWebhookParseResult.Of(eventName, instance, Renames(Items(root, "renamedTrackFiles")));

            case "Retag":
                return MediaWebhookParseResult.Of(eventName, instance, Additions(Paths(new[] { Child(root, "trackFile") })));

            case "ArtistDelete":
            case "AlbumDelete":
                return MediaWebhookParseResult.Of(eventName, instance,
                    Flag(root, "deletedFiles") ? FolderRemoval(Text(Child(root, "artist"), "path")) : Enumerable.Empty<MediaWebhookChange>());

            default:
                return MediaWebhookParseResult.Ignored(eventName, instance);
        }
    }
}
