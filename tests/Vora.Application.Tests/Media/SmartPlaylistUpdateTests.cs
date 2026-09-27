using NSubstitute;
using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Tests.Media;

public class SmartPlaylistUpdateTests
{
    private readonly ISmartPlaylistRepository _repo = Substitute.For<ISmartPlaylistRepository>();
    private readonly SmartPlaylistManager _manager;
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly SmartPlaylist _existing;

    public SmartPlaylistUpdateTests()
    {
        _manager = new SmartPlaylistManager(_repo, Substitute.For<ISmartPlaylistEvaluator>(), Substitute.For<IMusicRepository>());
        _existing = new SmartPlaylist
        {
            Id = Guid.NewGuid(),
            ProfileId = _profileId,
            Name = "Heavy Rotation",
            Description = "Played most",
            ArtworkUrl = "/api/artwork/custom/mix.jpg",
            MediaType = PlaylistMediaType.Music
        };
        _repo.GetByIdAsync(_existing.Id, _profileId).Returns(_existing);
    }

    private Task<SmartPlaylistSummaryVM?> SaveAsync(string? artworkUrl, string? description) =>
        _manager.UpdateAsync(_existing.Id, _profileId, new SmartPlaylistSaveRequest
        {
            Name = "Heavy Rotation",
            ArtworkUrl = artworkUrl,
            Description = description,
            MediaType = PlaylistMediaType.Music,
            Definition = new SmartPlaylistDefinition()
        });

    [Fact]
    public async Task Editing_the_rules_keeps_artwork_the_editor_never_sent()
    {
        await SaveAsync(artworkUrl: null, description: null);

        _existing.ArtworkUrl.Should().Be("/api/artwork/custom/mix.jpg");
        _existing.Description.Should().Be("Played most");
    }

    [Fact]
    public async Task A_new_artwork_url_replaces_the_old_one()
    {
        await SaveAsync(artworkUrl: "/api/artwork/custom/new.jpg", description: "Fresh");

        _existing.ArtworkUrl.Should().Be("/api/artwork/custom/new.jpg");
        _existing.Description.Should().Be("Fresh");
    }

    [Fact]
    public async Task An_empty_artwork_url_clears_it()
    {
        await SaveAsync(artworkUrl: "", description: "");

        _existing.ArtworkUrl.Should().BeEmpty();
        _existing.Description.Should().BeEmpty();
    }
}
