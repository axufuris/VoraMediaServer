using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.Arr;

namespace Vora.Plugins.Providers.Sonarr;

public class SonarrWebhookProvider : ArrWebhookProviderBase
{
    public SonarrWebhookProvider() : base("Sonarr")
    {
    }

    public override string Id => "sonarr_webhook";
    public override string Name => "Sonarr Webhook";
    public override string Description => "Lets Sonarr tell Vora when an episode file is imported, upgraded, renamed or deleted, so the library updates at once. Add a Webhook connection in Sonarr (Settings, Connect) pointing at Vora.";
    protected override LibraryKind Kind => LibraryKind.TvShow;
    public override string RequestServerProviderId => "sonarr_requester";
    public override string SetupInstructions =>
        "In Sonarr, open Settings, then Connect, and add a Webhook connection.\n" +
        "Paste the address above into URL and leave Method on POST.\n" +
        "Set Username to anything (such as vora) and Password to the secret above.\n" +
        "Tick On File Import, On File Upgrade, On Import Complete, On Rename, On Series Delete and On Episode File Delete.\n" +
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
                var added = Paths(new[] { Child(root, "episodeFile") }.Concat(Items(root, "episodeFiles"))).Distinct().ToList();
                var deleted = Paths(Items(root, "deletedFiles"));
                return MediaWebhookParseResult.Of(eventName, instance, Additions(added).Concat(Removals(deleted, added)));
            }

            case "Rename":
                return MediaWebhookParseResult.Of(eventName, instance, Renames(Items(root, "renamedEpisodeFiles")));

            case "EpisodeFileDelete":
                return MediaWebhookParseResult.Of(eventName, instance, Removals(Paths(new[] { Child(root, "episodeFile") }), Array.Empty<string>()));

            case "SeriesDelete":
                return MediaWebhookParseResult.Of(eventName, instance,
                    Flag(root, "deletedFiles") ? FolderRemoval(Text(Child(root, "series"), "path")) : Enumerable.Empty<MediaWebhookChange>());

            default:
                return MediaWebhookParseResult.Ignored(eventName, instance);
        }
    }
}
