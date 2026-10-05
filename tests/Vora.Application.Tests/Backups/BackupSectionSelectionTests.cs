using Vora.Application.Backups;
using Vora.Application.Backups.ViewModels;

namespace Vora.Application.Tests.Backups;

public class BackupSectionSelectionTests
{
    private static readonly string[] Available =
    [
        "settings.server",
        "users.profiles",
        "users.watch-history",
        "users.playlists",
        "podcasts.shows"
    ];

    [Fact]
    public void A_legacy_inclusion_list_becomes_an_exclusion_list_so_newer_sections_are_backed_up()
    {
        var settings = new BackupSettings { IncludedSectionKeys = new List<string> { "settings.server", "users.profiles" } };

        BackupSectionSelection.UpgradeLegacyInclusionList(settings);

        settings.IncludedSectionKeys.Should().BeNull();
        settings.ExcludedSectionKeys.Should().Contain("users.watch-history").And.NotContain("users.profiles");
        BackupSectionSelection.IsIncluded(settings, "users.playlists").Should().BeTrue();
        BackupSectionSelection.IsIncluded(settings, "podcasts.shows").Should().BeTrue();
        BackupSectionSelection.IsIncluded(settings, "users.watch-history").Should().BeFalse();
    }

    [Fact]
    public void Settings_without_a_legacy_list_are_left_alone()
    {
        var settings = new BackupSettings { ExcludedSectionKeys = new List<string> { "users.watch-history" } };

        BackupSectionSelection.UpgradeLegacyInclusionList(settings);

        settings.ExcludedSectionKeys.Should().Equal("users.watch-history");
    }

    [Fact]
    public void The_view_model_reports_every_section_as_included_until_one_is_excluded()
    {
        var everything = new BackupSettings();
        var withoutHistory = new BackupSettings { ExcludedSectionKeys = new List<string> { "USERS.WATCH-HISTORY" } };

        BackupSectionSelection.IncludedKeys(everything, Available).Should().BeNull();
        BackupSectionSelection.IncludedKeys(withoutHistory, Available).Should().Equal(
            "settings.server", "users.profiles", "users.playlists", "podcasts.shows");
    }

    [Fact]
    public void Saving_an_inclusion_list_stores_the_sections_left_out()
    {
        BackupSectionSelection.ExcludedKeys(new List<string> { "settings.server", "users.profiles" }, Available)
            .Should().Equal("users.watch-history", "users.playlists", "podcasts.shows");
        BackupSectionSelection.ExcludedKeys(Available.ToList(), Available).Should().BeNull();
        BackupSectionSelection.ExcludedKeys(null, Available).Should().BeNull();
        BackupSectionSelection.ExcludedKeys(new List<string>(), Available).Should().BeNull();
    }

    [Fact]
    public void Mapper_round_trips_the_picker_selection_through_the_exclusion_list()
    {
        var available = Available.Select(k => new AvailableSectionVM { Key = k }).ToList();
        var request = new BackupSettingsVM { IncludedSectionKeys = new List<string> { "settings.server", "users.profiles", "users.playlists", "podcasts.shows" } };

        var stored = BackupSettingsMapper.FromVM(request, new BackupSettings(), Available);
        var vm = BackupSettingsMapper.ToVM(stored, "/backups", available, TimeZoneInfo.Utc, DateTime.UtcNow);

        stored.ExcludedSectionKeys.Should().Equal("users.watch-history");
        vm.IncludedSectionKeys.Should().Equal(request.IncludedSectionKeys);
        vm.ScheduleTimeZone.Should().Be(TimeZoneInfo.Utc.Id);
    }
}
