using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Ai;
using Vora.Application.Plugins;
using Vora.Application.Settings;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Plugins;

// Plugins have always declared the library kinds they apply to and nothing ever
// read it, so a film's artwork picker listed the music artwork providers next to
// the film ones.
public class PluginOptionLibraryKindTests
{
    private sealed class FakePlugin : IVoraPlugin
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public string Version => "1.0.0";
        public string Description => string.Empty;
        public bool IsSystemPlugin => true;
        public required string Type { get; init; }
        public string DeveloperName => "test";
        public required IEnumerable<LibraryKind> Kinds { get; init; }
        public IEnumerable<LibraryKind> SupportedLibraryKinds => Kinds;
        public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => Array.Empty<PluginSettingDefinitionDto>();
    }

    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();

    private PluginManager Build(params IVoraPlugin[] plugins)
    {
        _settings.GetPluginSettingAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);
        return new PluginManager(
            plugins,
            _settings,
            Options.Create(new StoragePathsOptions()),
            Substitute.For<IOpenAiClient>(),
            NullLogger<PluginManager>.Instance);
    }

    private static FakePlugin Artwork(string id, params LibraryKind[] kinds) =>
        new() { Id = id, Name = id, Type = "Artwork", Kinds = kinds };

    [Fact]
    public async Task Returns_only_the_providers_that_apply_to_the_requested_kind()
    {
        var manager = Build(
            Artwork("film_art", LibraryKind.Movie, LibraryKind.TvShow),
            Artwork("music_art", LibraryKind.Music));

        var forFilm = await manager.GetPluginOptionsAsync("Artwork", "Movie");

        forFilm.Select(o => o.Id).Should().BeEquivalentTo(new[] { "film_art" });
    }

    [Fact]
    public async Task Returns_the_music_providers_for_a_music_library()
    {
        var manager = Build(
            Artwork("film_art", LibraryKind.Movie, LibraryKind.TvShow),
            Artwork("music_art", LibraryKind.Music));

        var forMusic = await manager.GetPluginOptionsAsync("Artwork", "Music");

        forMusic.Select(o => o.Id).Should().BeEquivalentTo(new[] { "music_art" });
    }

    // A caller that genuinely wants everything — the plugin admin page — must
    // keep working, so the filter is opt-in.
    [Fact]
    public async Task Returns_everything_when_no_kind_is_asked_for()
    {
        var manager = Build(
            Artwork("film_art", LibraryKind.Movie),
            Artwork("music_art", LibraryKind.Music));

        var all = await manager.GetPluginOptionsAsync("Artwork");

        all.Select(o => o.Id).Should().BeEquivalentTo(new[] { "film_art", "music_art" });
    }

    [Fact]
    public async Task Matches_a_kind_regardless_of_casing()
    {
        var manager = Build(Artwork("music_art", LibraryKind.Music));

        var result = await manager.GetPluginOptionsAsync("Artwork", "music");

        result.Select(o => o.Id).Should().BeEquivalentTo(new[] { "music_art" });
    }

    // A plugin that declares nothing means "anywhere", which is what the
    // interface default already says. Dropping those would hide working plugins.
    [Fact]
    public async Task Keeps_a_plugin_that_declares_no_kinds()
    {
        var manager = Build(new FakePlugin { Id = "anything", Name = "anything", Type = "Artwork", Kinds = Array.Empty<LibraryKind>() });

        var result = await manager.GetPluginOptionsAsync("Artwork", "Music");

        result.Select(o => o.Id).Should().BeEquivalentTo(new[] { "anything" });
    }

    // A picker that narrows results by provider matches on ProviderName, which is
    // what a result calls itself — "Fanart.tv" where Name is "Fanart.tv Music
    // Artwork". Surfacing only Name would give it nothing to match on.
    [Fact]
    public async Task Carries_the_name_a_result_identifies_itself_by()
    {
        var manager = Build(Artwork("music_art", LibraryKind.Music));

        var option = (await manager.GetPluginOptionsAsync("Artwork", "Music")).Single();

        option.ProviderName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Still_filters_by_plugin_type_first()
    {
        var manager = Build(
            Artwork("music_art", LibraryKind.Music),
            new FakePlugin { Id = "music_meta", Name = "music_meta", Type = "Metadata", Kinds = new[] { LibraryKind.Music } });

        var result = await manager.GetPluginOptionsAsync("Artwork", "Music");

        result.Select(o => o.Id).Should().BeEquivalentTo(new[] { "music_art" });
    }
}
