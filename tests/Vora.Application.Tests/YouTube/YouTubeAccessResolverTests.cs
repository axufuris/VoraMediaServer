using Vora.Application.YouTube;
using Vora.Application.YouTube.Dtos;
using Vora.Domain.Entities.Users;
using Vora.Domain.Entities.YouTube;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.YouTube;

namespace Vora.Application.Tests.YouTube;

public class YouTubeAccessResolverTests
{
    private readonly IYouTubeAccessRepository _repository;
    private readonly IYouTubeDataApiClient _apiClient;
    private readonly IPluginSettingsProvider _settings;
    private readonly IVoraPlugin _youTubePlugin;

    public YouTubeAccessResolverTests()
    {
        _repository = Substitute.For<IYouTubeAccessRepository>();
        _apiClient = Substitute.For<IYouTubeDataApiClient>();
        _settings = Substitute.For<IPluginSettingsProvider>();
        _youTubePlugin = Substitute.For<IVoraPlugin>();
        _youTubePlugin.Id.Returns(YouTubePlugin.PluginId);
    }

    private YouTubeAccessResolver BuildResolver(bool includePlugin = true)
    {
        var plugins = includePlugin ? new[] { _youTubePlugin } : Array.Empty<IVoraPlugin>();
        return new YouTubeAccessResolver(_repository, _apiClient, _settings, plugins);
    }

    private void StubHappyPath(UserProfile profile, YouTubeAccountSettings? account = null, YouTubeProfileSettings? profileSettings = null)
    {
        _apiClient.IsConfiguredAsync().Returns(true);
        _settings.GetSettingAsync(YouTubePlugin.PluginId, YouTubePlugin.IsEnabledSettingKey).Returns((string?)null);
        _repository.GetProfileWithUserAsync(profile.Id).Returns(profile);
        _repository.GetAccountSettingsAsync(profile.UserId).Returns(account);
        _repository.GetProfileSettingsAsync(profile.Id).Returns(profileSettings);
    }

    private static UserProfile NewProfile(bool allRatings = true, bool blockUnrated = false)
    {
        return new UserProfile
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            UserId = Guid.NewGuid(),
            BlockUnratedContent = blockUnrated,
            AllowedMovieRatings = allRatings ? new List<string>() : new List<string> { "PG", "PG-13" },
            AllowedTvRatings = new List<string>(),
            AllowedMusicRatings = new List<string>()
        };
    }

    [Fact]
    public async Task ResolveAsync_denies_when_plugin_not_installed()
    {
        var resolver = BuildResolver(includePlugin: false);

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        result.IsAvailable.Should().BeFalse();
        result.DeniedReason.Should().Be("YouTube plugin is not installed.");
        await _apiClient.DidNotReceiveWithAnyArgs().IsConfiguredAsync();
    }

    [Fact]
    public async Task ResolveAsync_denies_when_api_key_not_configured()
    {
        _apiClient.IsConfiguredAsync().Returns(false);
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        result.IsAvailable.Should().BeFalse();
        result.DeniedReason.Should().Be("YouTube Data API key is not configured.");
        await _settings.DidNotReceiveWithAnyArgs().GetSettingAsync(string.Empty, string.Empty);
    }

    [Fact]
    public async Task ResolveAsync_denies_when_server_setting_explicitly_false()
    {
        _apiClient.IsConfiguredAsync().Returns(true);
        _settings.GetSettingAsync(YouTubePlugin.PluginId, YouTubePlugin.IsEnabledSettingKey).Returns("false");
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        result.IsAvailable.Should().BeFalse();
        result.DeniedReason.Should().Be("YouTube is disabled server-wide.");
        await _repository.DidNotReceiveWithAnyArgs().GetProfileWithUserAsync(default);
    }

    [Fact]
    public async Task ResolveAsync_treats_missing_server_setting_as_enabled()
    {
        var profile = NewProfile();
        StubHappyPath(profile);
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(profile.Id);

        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_denies_when_profile_not_found()
    {
        _apiClient.IsConfiguredAsync().Returns(true);
        _settings.GetSettingAsync(YouTubePlugin.PluginId, YouTubePlugin.IsEnabledSettingKey).Returns((string?)null);
        _repository.GetProfileWithUserAsync(Arg.Any<Guid>()).Returns((UserProfile?)null);
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        result.IsAvailable.Should().BeFalse();
        result.DeniedReason.Should().Be("Profile not found.");
    }

    [Fact]
    public async Task ResolveAsync_denies_when_account_disabled()
    {
        var profile = NewProfile();
        StubHappyPath(profile, account: new YouTubeAccountSettings
        {
            AccountId = profile.UserId,
            YouTubeAccess = YouTubeAccessSetting.Disabled
        });
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(profile.Id);

        result.IsAvailable.Should().BeFalse();
        result.DeniedReason.Should().Be("YouTube is disabled for this account.");
    }

    [Fact]
    public async Task ResolveAsync_allows_when_account_setting_is_inherit()
    {
        var profile = NewProfile();
        StubHappyPath(profile, account: new YouTubeAccountSettings
        {
            AccountId = profile.UserId,
            YouTubeAccess = YouTubeAccessSetting.Inherit
        });
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(profile.Id);

        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_denies_when_profile_setting_disabled()
    {
        var profile = NewProfile();
        StubHappyPath(profile, profileSettings: new YouTubeProfileSettings
        {
            UserProfileId = profile.Id,
            IsEnabled = false
        });
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(profile.Id);

        result.IsAvailable.Should().BeFalse();
        result.DeniedReason.Should().Be("YouTube is disabled for this profile.");
    }

    [Fact]
    public async Task ResolveAsync_success_with_no_parental_controls_uses_moderate_safe_search()
    {
        var profile = NewProfile(allRatings: true, blockUnrated: false);
        StubHappyPath(profile);
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(profile.Id);

        result.IsAvailable.Should().BeTrue();
        result.SafeSearch.Should().Be(YouTubeSafeSearchLevel.Moderate);
        result.FilterAgeRestricted.Should().BeFalse();
        result.BlockUnratedContent.Should().BeFalse();
        result.HasAllRatings.Should().BeTrue();
        result.AllowedMovieRatings.Should().BeEmpty();
        result.AllowedTvRatings.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_success_with_movie_rating_restriction_uses_strict_safe_search()
    {
        var profile = NewProfile(allRatings: false);
        StubHappyPath(profile);
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(profile.Id);

        result.IsAvailable.Should().BeTrue();
        result.SafeSearch.Should().Be(YouTubeSafeSearchLevel.Strict);
        result.FilterAgeRestricted.Should().BeTrue();
        result.HasAllRatings.Should().BeFalse();
        result.AllowedMovieRatings.Should().BeEquivalentTo(new[] { "PG", "PG-13" });
    }

    [Fact]
    public async Task ResolveAsync_success_with_block_unrated_uses_strict_safe_search()
    {
        var profile = NewProfile(allRatings: true, blockUnrated: true);
        StubHappyPath(profile);
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(profile.Id);

        result.IsAvailable.Should().BeTrue();
        result.SafeSearch.Should().Be(YouTubeSafeSearchLevel.Strict);
        result.FilterAgeRestricted.Should().BeTrue();
        result.BlockUnratedContent.Should().BeTrue();
        result.HasAllRatings.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_returns_copies_of_rating_lists_not_references()
    {
        var profile = NewProfile(allRatings: false);
        StubHappyPath(profile);
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(profile.Id);

        result.AllowedMovieRatings.Should().NotBeSameAs(profile.AllowedMovieRatings);
    }

    [Fact]
    public async Task ResolveAsync_plugin_id_match_is_case_insensitive()
    {
        _youTubePlugin.Id.Returns(YouTubePlugin.PluginId.ToUpperInvariant());
        var profile = NewProfile();
        StubHappyPath(profile);
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(profile.Id);

        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_server_setting_false_is_case_insensitive()
    {
        _apiClient.IsConfiguredAsync().Returns(true);
        _settings.GetSettingAsync(YouTubePlugin.PluginId, YouTubePlugin.IsEnabledSettingKey).Returns("FALSE");
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(Guid.NewGuid());

        result.IsAvailable.Should().BeFalse();
        result.DeniedReason.Should().Be("YouTube is disabled server-wide.");
    }
}
