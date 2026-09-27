using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Plugins;
using Vora.Application.Settings;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Plugins;

public class PluginSettingsEnvSeederTests
{
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IVoraPlugin _pluginA;
    private readonly IVoraPlugin _pluginB;

    public PluginSettingsEnvSeederTests()
    {
        _settingsRepo = Substitute.For<ISystemSettingsRepository>();

        _pluginA = Substitute.For<IVoraPlugin>();
        _pluginA.Id.Returns("plugin_a");
        _pluginA.GetSettingDefinitions().Returns(new List<PluginSettingDefinitionDto>
        {
            new() { Key = "api_key" },
            new() { Key = "region" }
        });

        _pluginB = Substitute.For<IVoraPlugin>();
        _pluginB.Id.Returns("plugin_b");
        _pluginB.GetSettingDefinitions().Returns(new List<PluginSettingDefinitionDto>
        {
            new() { Key = "token" }
        });
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private PluginSettingsEnvSeeder BuildSeeder(IConfiguration config, params IVoraPlugin[] plugins) =>
        new(config, plugins.Length == 0 ? new[] { _pluginA, _pluginB } : plugins, _settingsRepo, NullLogger<PluginSettingsEnvSeeder>.Instance);

    [Fact]
    public async Task SeedAsync_writes_setting_when_no_existing_value_present()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:plugin_a:api_key"] = "secret-key"
        });
        _settingsRepo.GetPluginSettingAsync("plugin_a", "api_key").Returns((string?)null);

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.Received(1).SetPluginSettingAsync("plugin_a", "api_key", "secret-key");
    }

    [Fact]
    public async Task SeedAsync_does_not_overwrite_existing_db_value()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:plugin_a:api_key"] = "env-key"
        });
        _settingsRepo.GetPluginSettingAsync("plugin_a", "api_key").Returns("db-key");

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.DidNotReceive().SetPluginSettingAsync("plugin_a", "api_key", Arg.Any<string>());
    }

    [Fact]
    public async Task SeedAsync_skips_unknown_plugin()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:not_installed:api_key"] = "value"
        });

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.DidNotReceiveWithAnyArgs().SetPluginSettingAsync(string.Empty, string.Empty, string.Empty);
    }

    [Fact]
    public async Task SeedAsync_skips_unknown_setting_key_for_known_plugin()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:plugin_a:bogus_key"] = "value"
        });

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.DidNotReceiveWithAnyArgs().SetPluginSettingAsync(string.Empty, string.Empty, string.Empty);
    }

    [Fact]
    public async Task SeedAsync_accepts_is_enabled_key_even_when_not_in_plugin_definitions()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:plugin_a:is_enabled"] = "true"
        });
        _settingsRepo.GetPluginSettingAsync("plugin_a", "is_enabled").Returns((string?)null);

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.Received(1).SetPluginSettingAsync("plugin_a", "is_enabled", "true");
    }

    [Fact]
    public async Task SeedAsync_seeds_multiple_keys_across_multiple_plugins()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:plugin_a:api_key"] = "key-a",
            ["Vora:PluginSettings:plugin_a:region"] = "us",
            ["Vora:PluginSettings:plugin_b:token"] = "token-b"
        });
        _settingsRepo.GetPluginSettingAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.Received(1).SetPluginSettingAsync("plugin_a", "api_key", "key-a");
        await _settingsRepo.Received(1).SetPluginSettingAsync("plugin_a", "region", "us");
        await _settingsRepo.Received(1).SetPluginSettingAsync("plugin_b", "token", "token-b");
    }

    [Fact]
    public async Task SeedAsync_plugin_id_match_is_case_insensitive()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:PLUGIN_A:api_key"] = "key"
        });
        _settingsRepo.GetPluginSettingAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.Received(1).SetPluginSettingAsync("PLUGIN_A", "api_key", "key");
    }

    [Fact]
    public async Task SeedAsync_setting_key_match_is_case_insensitive()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:plugin_a:API_KEY"] = "key"
        });
        _settingsRepo.GetPluginSettingAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.Received(1).SetPluginSettingAsync("plugin_a", "API_KEY", "key");
    }

    [Fact]
    public async Task SeedAsync_no_op_when_section_absent()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.DidNotReceiveWithAnyArgs().GetPluginSettingAsync(string.Empty, string.Empty);
        await _settingsRepo.DidNotReceiveWithAnyArgs().SetPluginSettingAsync(string.Empty, string.Empty, string.Empty);
    }

    [Fact]
    public async Task SeedAsync_ignores_empty_value_for_known_key()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:plugin_a:api_key"] = ""
        });

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.DidNotReceiveWithAnyArgs().SetPluginSettingAsync(string.Empty, string.Empty, string.Empty);
    }

    [Fact]
    public async Task SeedAsync_partial_mix_of_seeded_and_skipped_only_seeds_eligible()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:plugin_a:api_key"] = "env-key",
            ["Vora:PluginSettings:plugin_a:region"] = "us"
        });
        _settingsRepo.GetPluginSettingAsync("plugin_a", "api_key").Returns("db-existing");
        _settingsRepo.GetPluginSettingAsync("plugin_a", "region").Returns((string?)null);

        await BuildSeeder(config).SeedAsync(TestContext.Current.CancellationToken);

        await _settingsRepo.DidNotReceive().SetPluginSettingAsync("plugin_a", "api_key", Arg.Any<string>());
        await _settingsRepo.Received(1).SetPluginSettingAsync("plugin_a", "region", "us");
    }

    [Fact]
    public async Task SeedAsync_respects_cancellation()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Vora:PluginSettings:plugin_a:api_key"] = "key"
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await BuildSeeder(config).SeedAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
