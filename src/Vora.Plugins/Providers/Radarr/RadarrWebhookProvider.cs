using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.Arr;

namespace Vora.Plugins.Providers.Radarr;

public class RadarrWebhookProvider : ArrWebhookProviderBase
{
    public RadarrWebhookProvider() : base("Radarr")
    {
    }

    public override string Id => "radarr_webhook";
    public override string Name => "Radarr Webhook";
    public override string Description => "Lets Radarr tell Vora when a movie file is imported, upgraded, renamed or deleted, so the library updates at once. Add a Webhook connection in Radarr (Settings, Connect) pointing at Vora.";
    protected override LibraryKind Kind => LibraryKind.Movie;
    public override string RequestServerProviderId => "radarr_requester";
    public override string SetupInstructions =>
        "In Radarr, open Settings, then Connect, and add a Webhook connection.\n" +
        "Paste the address above into URL and leave Method on POST.\n" +
        "Set Username to anything (such as vora) and Password to the secret above.\n" +
        "Tick On File Import, On File Upgrade, On Rename, On Movie Delete and On Movie File Delete.\n" +
        "Press Test. It should appear under Recent deliveries as a test received.";

    public override MediaWebhookParseResult Parse(string body, IReadOnlyDictionary<string, string> settings)
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
                var added = Paths(new[] { Child(root, "movieFile") }).ToList();
                var deleted = Paths(Items(root, "deletedFiles"));
                return MediaWebhookParseResult.Of(eventName, instance, Additions(added).Concat(Removals(deleted, added)));
            }

            case "Rename":
                return MediaWebhookParseResult.Of(eventName, instance, Renames(Items(root, "renamedMovieFiles")));

            case "MovieFileDelete":
                return MediaWebhookParseResult.Of(eventName, instance, Removals(Paths(new[] { Child(root, "movieFile") }), Array.Empty<string>()));

            case "MovieDelete":
                return MediaWebhookParseResult.Of(eventName, instance,
                    Flag(root, "deletedFiles") ? FolderRemoval(Text(Child(root, "movie"), "folderPath")) : Enumerable.Empty<MediaWebhookChange>());

            default:
                return MediaWebhookParseResult.Ignored(eventName, instance);
        }
    }
}
