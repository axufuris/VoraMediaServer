using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Plugins;
using Vora.Application.Settings;
using Vora.Plugins.Providers.OpenSubtitles;

namespace Vora.Application.Tests.Subtitles;

// Every plugin is meant to be configurable from Docker without an admin ever
// opening the settings page. That works only if the keys the seeder accepts are
// exactly the keys the plugin declares — so this drives the seeder against the
// REAL provider rather than a stub, which is the only way a typo in a setting
// key or a missing definition would show up.
public class OpenSubtitlesEnvSeedingTests
{
    private const string Prefix = "Vora:PluginSettings:opensubtitles_search:";

    private readonly ISystemSettingsRepository _settingsRepo = Substitute.For<ISystemSettingsRepository>();

    private static OpenSubtitlesSubtitleProvider RealProvider() => new(
        Substitute.For<IHttpClientFactory>(),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<OpenSubtitlesSubtitleProvider>.Instance);

    private async Task<List<(string Key, string Value)>> SeedAsync(Dictionary<string, string?> env)
    {
        var written = new List<(string, string)>();
        await _settingsRepo.SetPluginSettingAsync(
            Arg.Do<string>(_ => { }),
            Arg.Do<string>(_ => { }),
            Arg.Do<string>(_ => { }));
        _settingsRepo.When(r => r.SetPluginSettingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
            .Do(call => written.Add((call.ArgAt<string>(1), call.ArgAt<string>(2))));

        var config = new ConfigurationBuilder().AddInMemoryCollection(env).Build();
        var seeder = new PluginSettingsEnvSeeder(config, [RealProvider()], _settingsRepo, NullLogger<PluginSettingsEnvSeeder>.Instance);

        await seeder.SeedAsync();
        return written;
    }

    [Fact]
    public async Task Every_setting_the_plugin_declares_can_be_seeded_from_the_environment()
    {
        var declared = RealProvider().GetSettingDefinitions().Select(d => d.Key).ToList();
        var env = declared.ToDictionary(key => Prefix + key, key => (string?)$"value-for-{key}");

        var written = await SeedAsync(env);

        written.Select(w => w.Key).Should().BeEquivalentTo(declared);
    }

    [Theory]
    [InlineData("api_key")]
    [InlineData("auth_mode")]
    [InlineData("username")]
    [InlineData("password")]
    [InlineData("default_languages")]
    public async Task The_documented_keys_are_the_keys_the_plugin_actually_declares(string key)
    {
        var written = await SeedAsync(new Dictionary<string, string?> { [Prefix + key] = "x" });

        written.Should().ContainSingle().Which.Key.Should().Be(key);
    }

    // The seeder skips a key the plugin does not declare, so a documented name
    // that drifted from the code would silently do nothing.
    [Fact]
    public async Task A_key_the_plugin_does_not_declare_is_not_written()
    {
        var written = await SeedAsync(new Dictionary<string, string?> { [Prefix + "apikey"] = "x" });

        written.Should().BeEmpty();
    }

    // The auth mode arrives from an env var as free text, not as a click on the
    // settings page, so the parser has to accept the plain word as readily as
    // the label the dropdown stores.
    [Theory]
    [InlineData("account")]
    [InlineData("Account")]
    [InlineData("Account (username/password)")]
    public void An_env_seeded_auth_mode_selects_account(string configured)
    {
        OpenSubtitlesAuthPlan.ParseMode(configured).Should().Be(OpenSubtitlesAuthMode.Account);
    }

    [Theory]
    [InlineData("apikey")]
    [InlineData("API key only")]
    public void Anything_else_stays_on_the_api_key(string configured)
    {
        OpenSubtitlesAuthPlan.ParseMode(configured).Should().Be(OpenSubtitlesAuthMode.ApiKeyOnly);
    }

    [Fact]
    public async Task The_plugin_can_be_disabled_from_the_environment_like_any_other()
    {
        var written = await SeedAsync(new Dictionary<string, string?> { [Prefix + "is_enabled"] = "false" });

        written.Should().ContainSingle().Which.Key.Should().Be("is_enabled");
    }
}
