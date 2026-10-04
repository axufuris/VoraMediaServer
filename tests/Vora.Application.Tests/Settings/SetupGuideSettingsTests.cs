using System.Text.Json;
using Vora.Application.Settings;
using Vora.Application.Settings.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Settings;

public class SetupGuideSettingsTests
{
    private readonly ISystemSettingsRepository _repo = Substitute.For<ISystemSettingsRepository>();
    private readonly ServerSetting _settings = new();
    private readonly SystemSettingsManager _manager;

    public SetupGuideSettingsTests()
    {
        _repo.GetSettingsAsync().Returns(_settings);
        _repo.GetSettingsForUpdateAsync().Returns(_settings);
        _manager = new SystemSettingsManager(_repo, Array.Empty<IVoraPlugin>(), Substitute.For<IServiceProvider>());
    }

    [Fact]
    public async Task A_new_server_has_not_started_the_guide_and_plans_movies_shows_and_music()
    {
        var guide = await _manager.GetSetupGuideAsync();

        guide.Status.Should().Be(SetupGuideStatus.NotStarted);
        guide.Step.Should().BeNull();
        guide.MoviesAndShows.Should().BeTrue();
        guide.Music.Should().BeTrue();
        guide.LiveTv.Should().BeFalse();
        guide.InternetRadio.Should().BeFalse();
        guide.Podcasts.Should().BeFalse();
    }

    [Fact]
    public async Task Progress_and_content_choices_are_saved()
    {
        var saved = await _manager.UpdateSetupGuideAsync(new SetupGuideVM
        {
            Status = SetupGuideStatus.InProgress,
            Step = " artwork ",
            MoviesAndShows = true,
            LiveTv = true,
            Podcasts = true
        });

        _settings.SetupGuideStatus.Should().Be(SetupGuideStatus.InProgress);
        _settings.SetupGuideStep.Should().Be("artwork");
        _settings.SetupGuideContent.Should().Be(SetupGuideContent.MoviesAndShows | SetupGuideContent.LiveTv | SetupGuideContent.Podcasts);
        saved.Music.Should().BeFalse();
        saved.LiveTv.Should().BeTrue();
        await _repo.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task An_overlong_step_is_cut_to_the_column_and_a_blank_one_is_cleared()
    {
        await _manager.UpdateSetupGuideAsync(new SetupGuideVM { Status = SetupGuideStatus.InProgress, Step = new string('x', 200) });
        _settings.SetupGuideStep.Should().HaveLength(SetupGuideVM.MaxStepLength);

        await _manager.UpdateSetupGuideAsync(new SetupGuideVM { Status = SetupGuideStatus.Completed, Step = "  " });
        _settings.SetupGuideStep.Should().BeNull();
        _settings.SetupGuideStatus.Should().Be(SetupGuideStatus.Completed);
    }

    [Fact]
    public void A_save_that_leaves_a_field_out_falls_back_to_the_servers_own_default()
    {
        var fromEntity = JsonSerializer.SerializeToElement(ServerSettingsVM.Projection.Compile()(new ServerSetting()));
        var fallback = JsonSerializer.SerializeToElement(new ServerSettingsVM());

        var differences = fromEntity.EnumerateObject()
            .Where(p => p.Value.GetRawText() != fallback.GetProperty(p.Name).GetRawText())
            .Select(p => $"{p.Name}: server {p.Value.GetRawText()}, save fallback {fallback.GetProperty(p.Name).GetRawText()}")
            .ToList();

        differences.Should().BeEmpty();
    }
}
