using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Ai;
using Vora.Application.FileSystem;
using Vora.Application.Plugins;
using Vora.Application.Settings;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Plugins;

public class PluginManagerTests
{
    private readonly ISystemSettingsRepository _settings;
    private readonly IOptions<StoragePathsOptions> _storage;
    private readonly IOpenAiClient _openAi;

    public PluginManagerTests()
    {
        _settings = Substitute.For<ISystemSettingsRepository>();
        _storage = Options.Create(new StoragePathsOptions());
        _openAi = Substitute.For<IOpenAiClient>();
        _openAi.IsConfiguredAsync().Returns(true);
    }

    private PluginManager Build(params IVoraPlugin[] plugins) =>
        new(plugins, _settings, _storage, _openAi, NullLogger<PluginManager>.Instance);

    private static IVoraPlugin MakePlugin(string id, string type = "Metadata", bool isSystem = false, bool isAi = false,
        List<PluginSettingDefinitionDto>? definitions = null)
    {
        var plugin = Substitute.For<IVoraPlugin>();
        plugin.Id.Returns(id);
        plugin.Name.Returns($"{id}-name");
        plugin.Version.Returns("1.0.0");
        plugin.Description.Returns("desc");
        plugin.Type.Returns(type);
        plugin.IsSystemPlugin.Returns(isSystem);
        plugin.IsAiPlugin.Returns(isAi);
        plugin.GetSettingDefinitions().Returns(definitions ?? new List<PluginSettingDefinitionDto>());
        return plugin;
    }

    [Fact]
    public async Task GetActivePluginsAsync_returns_plugin_with_enabled_true_when_setting_absent()
    {
        var plugin = MakePlugin("p1");
        _settings.GetPluginSettingAsync("p1", "is_enabled").Returns((string?)null);

        var result = (await Build(plugin).GetActivePluginsAsync()).ToList();

        result.Should().ContainSingle();
        result[0].Id.Should().Be("p1");
        result[0].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetActivePluginsAsync_returns_enabled_false_when_setting_is_literal_false()
    {
        var plugin = MakePlugin("p1");
        _settings.GetPluginSettingAsync("p1", "is_enabled").Returns("false");

        var result = (await Build(plugin).GetActivePluginsAsync()).ToList();

        result[0].IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetActivePluginsAsync_returns_enabled_true_for_any_non_false_value()
    {
        var plugin = MakePlugin("p1");
        _settings.GetPluginSettingAsync("p1", "is_enabled").Returns("true");

        var result = (await Build(plugin).GetActivePluginsAsync()).ToList();

        result[0].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetActivePluginsAsync_maps_all_plugin_metadata()
    {
        var plugin = MakePlugin("p1", type: "Metadata", isSystem: true, isAi: true);
        plugin.DeveloperName.Returns("Acme");
        plugin.DocumentationUrl.Returns("https://docs.example.com");
        plugin.LatestVersionApiUrl.Returns("https://api.example.com/version");

        var result = (await Build(plugin).GetActivePluginsAsync()).ToList();

        result[0].Name.Should().Be("p1-name");
        result[0].Version.Should().Be("1.0.0");
        result[0].Type.Should().Be("Metadata");
        result[0].IsSystemPlugin.Should().BeTrue();
        result[0].IsAiPlugin.Should().BeTrue();
        result[0].DeveloperName.Should().Be("Acme");
        result[0].DocumentationUrl.Should().Be("https://docs.example.com");
        result[0].LatestVersionApiUrl.Should().Be("https://api.example.com/version");
    }

    private sealed class HintPlugin : IVoraPlugin
    {
        public string Id => "hint";
        public string Name => "Hint";
        public string Version => "1.0";
        public string Description => "d";
        public bool IsSystemPlugin => false;
        public string Type => "Metadata";
        public string? ExternalConfigurationHint => "Configure under System Settings → Request Servers.";
        public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();
    }

    [Fact]
    public async Task GetActivePluginsAsync_maps_external_configuration_hint()
    {
        var result = (await Build(new HintPlugin()).GetActivePluginsAsync()).ToList();

        result[0].ExternalConfigurationHint.Should().Be("Configure under System Settings → Request Servers.");
    }

    [Fact]
    public async Task GetActivePluginsAsync_has_settings_is_false_when_plugin_has_no_definitions()
    {
        var plugin = MakePlugin("p1");

        var result = (await Build(plugin).GetActivePluginsAsync()).ToList();

        result[0].HasSettings.Should().BeFalse();
        result[0].RequiresConfiguration.Should().BeFalse();
    }

    [Fact]
    public async Task GetActivePluginsAsync_requires_configuration_when_a_no_default_field_is_blank()
    {
        var plugin = MakePlugin("p1", definitions: new List<PluginSettingDefinitionDto>
        {
            new() { Key = "api_key" }
        });
        _settings.GetAllPluginSettingsAsync("p1").Returns(new Dictionary<string, string>());

        var result = (await Build(plugin).GetActivePluginsAsync()).ToList();

        result[0].HasSettings.Should().BeTrue();
        result[0].RequiresConfiguration.Should().BeTrue();
    }

    [Fact]
    public async Task GetActivePluginsAsync_does_not_require_configuration_when_required_field_is_filled()
    {
        var plugin = MakePlugin("p1", definitions: new List<PluginSettingDefinitionDto>
        {
            new() { Key = "api_key" }
        });
        _settings.GetAllPluginSettingsAsync("p1").Returns(new Dictionary<string, string> { ["api_key"] = "abc123" });

        var result = (await Build(plugin).GetActivePluginsAsync()).ToList();

        result[0].RequiresConfiguration.Should().BeFalse();
    }

    [Fact]
    public async Task GetActivePluginsAsync_does_not_require_configuration_for_blank_field_that_has_a_default()
    {
        var plugin = MakePlugin("p1", definitions: new List<PluginSettingDefinitionDto>
        {
            new() { Key = "region", DefaultValue = "US" }
        });
        _settings.GetAllPluginSettingsAsync("p1").Returns(new Dictionary<string, string>());

        var result = (await Build(plugin).GetActivePluginsAsync()).ToList();

        result[0].RequiresConfiguration.Should().BeFalse();
    }

    [Fact]
    public async Task GetPluginOptionsAsync_filters_by_type_case_insensitively()
    {
        var a = MakePlugin("a", type: "Metadata");
        var b = MakePlugin("b", type: "metadata");
        var c = MakePlugin("c", type: "Artwork");

        var result = (await Build(a, b, c).GetPluginOptionsAsync("Metadata")).ToList();

        result.Select(o => o.Id).Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public async Task GetPluginOptionsAsync_orders_by_id()
    {
        var z = MakePlugin("z", type: "Metadata");
        var a = MakePlugin("a", type: "Metadata");
        var m = MakePlugin("m", type: "Metadata");

        var result = (await Build(z, a, m).GetPluginOptionsAsync("Metadata")).ToList();

        result.Select(o => o.Id).Should().Equal("a", "m", "z");
    }

    [Fact]
    public async Task GetPluginOptionsAsync_excludes_disabled_plugins()
    {
        var enabled = MakePlugin("on", type: "Metadata");
        var disabled = MakePlugin("off", type: "Metadata");
        _settings.GetPluginSettingAsync("off", "is_enabled").Returns("false");

        var result = (await Build(enabled, disabled).GetPluginOptionsAsync("Metadata")).ToList();

        result.Select(o => o.Id).Should().Equal("on");
    }

    [Fact]
    public async Task GetPluginOptionsAsync_excludes_plugin_with_missing_required_setting()
    {
        var plugin = MakePlugin("p1", type: "Metadata", definitions: new List<PluginSettingDefinitionDto>
        {
            new() { Key = "api_key" }
        });
        _settings.GetAllPluginSettingsAsync("p1").Returns(new Dictionary<string, string>());

        var result = (await Build(plugin).GetPluginOptionsAsync("Metadata")).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPluginOptionsAsync_excludes_plugin_with_whitespace_required_setting()
    {
        var plugin = MakePlugin("p1", type: "Metadata", definitions: new List<PluginSettingDefinitionDto>
        {
            new() { Key = "api_key" }
        });
        _settings.GetAllPluginSettingsAsync("p1").Returns(new Dictionary<string, string> { ["api_key"] = "   " });

        var result = (await Build(plugin).GetPluginOptionsAsync("Metadata")).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPluginOptionsAsync_includes_plugin_with_no_required_settings()
    {
        var plugin = MakePlugin("p1", type: "Metadata", definitions: new List<PluginSettingDefinitionDto>());

        var result = (await Build(plugin).GetPluginOptionsAsync("Metadata")).ToList();

        result.Should().ContainSingle();
        result[0].ExternalIdLabel.Should().Be("ID");
        result[0].ExternalIdPlaceholder.Should().Be("Enter ID");
    }

    [Fact]
    public async Task GetPluginOptionsAsync_excludes_ai_plugin_when_openai_key_not_configured()
    {
        _openAi.IsConfiguredAsync().Returns(false);
        var plugin = MakePlugin("ai", type: "Metadata", isSystem: true, isAi: true);

        var result = (await Build(plugin).GetPluginOptionsAsync("Metadata")).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPluginOptionsAsync_includes_ai_plugin_when_openai_key_configured()
    {
        _openAi.IsConfiguredAsync().Returns(true);
        var plugin = MakePlugin("ai", type: "Metadata", isSystem: true, isAi: true);

        var result = (await Build(plugin).GetPluginOptionsAsync("Metadata")).ToList();

        result.Should().ContainSingle();
        result[0].Id.Should().Be("ai");
    }

    [Fact]
    public async Task GetPluginOptionsAsync_uses_chronology_provider_label_when_plugin_is_chronology_provider()
    {
        var plugin = Substitute.For<IChronologyProvider>();
        plugin.Id.Returns("chrono");
        plugin.Name.Returns("ChronoProvider");
        plugin.Type.Returns("Chronology");
        plugin.Version.Returns("1.0");
        plugin.Description.Returns("d");
        plugin.GetSettingDefinitions().Returns(new List<PluginSettingDefinitionDto>());
        plugin.ExternalIdLabel.Returns("TMDB Collection ID");
        plugin.ExternalIdPlaceholder.Returns("e.g. 10");

        var result = (await Build(plugin).GetPluginOptionsAsync("Chronology")).ToList();

        result.Should().ContainSingle();
        result[0].ExternalIdLabel.Should().Be("TMDB Collection ID");
        result[0].ExternalIdPlaceholder.Should().Be("e.g. 10");
    }

    [Fact]
    public async Task UploadPluginAsync_rejects_non_dll_files()
    {
        var manager = Build();
        await using var stream = new MemoryStream();
        var file = new UploadedFile(stream, "evil.exe");

        var act = async () => await manager.UploadPluginAsync(file);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*.dll*");
    }

    [Fact]
    public async Task UploadPluginAsync_writes_dll_to_configured_plugins_path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "vora-plugin-test-" + Guid.NewGuid());
        try
        {
            var options = Options.Create(new StoragePathsOptions { Plugins = tempDir });
            var manager = new PluginManager(Array.Empty<IVoraPlugin>(), _settings, options, _openAi, NullLogger<PluginManager>.Instance);

            var content = new byte[] { 1, 2, 3, 4 };
            await using var stream = new MemoryStream(content);
            var file = new UploadedFile(stream, "my-plugin.dll");

            await manager.UploadPluginAsync(file);

            var expectedPath = Path.Combine(tempDir, "my-plugin.dll");
            File.Exists(expectedPath).Should().BeTrue();
            File.ReadAllBytes(expectedPath).Should().Equal(content);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void UninstallPlugin_returns_false_for_unknown_id()
    {
        var manager = Build(MakePlugin("known"));

        manager.UninstallPlugin("unknown").Should().BeFalse();
    }

    [Fact]
    public void UninstallPlugin_throws_for_system_plugin()
    {
        var systemPlugin = MakePlugin("sys", isSystem: true);
        var manager = Build(systemPlugin);

        var act = () => manager.UninstallPlugin("sys");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*system*");
    }

}
