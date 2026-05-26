using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Iptv;
using Vora.Application.Iptv.Dtos;
using Vora.Application.Settings;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class DvrManagerTests
{
    private readonly IIptvRepository _repo;
    private readonly IIptvEpgService _epg;
    private readonly IClientNotifier _notifier;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly DvrManager _manager;

    public DvrManagerTests()
    {
        _repo = Substitute.For<IIptvRepository>();
        _epg = Substitute.For<IIptvEpgService>();
        _notifier = Substitute.For<IClientNotifier>();
        _settingsRepo = Substitute.For<ISystemSettingsRepository>();
        _manager = new DvrManager(_repo, _epg, NullLogger<DvrManager>.Instance, _notifier, _settingsRepo);
    }

    private static UserProfile MakeProfile(bool isAdmin = false, bool canRecord = false)
    {
        return new UserProfile
        {
            Id = Guid.NewGuid(),
            Name = "p",
            UserId = Guid.NewGuid(),
            IsAdmin = isAdmin,
            CanRecordLiveTv = canRecord
        };
    }

    [Fact]
    public async Task ScheduleRecordingAsync_throws_when_profile_missing()
    {
        _repo.GetUserProfileAsync(Arg.Any<Guid>()).Returns((UserProfile?)null);

        var act = async () => await _manager.ScheduleRecordingAsync(Guid.NewGuid(), Guid.NewGuid(), "Title", null, false);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Profile not found*");
    }

    [Fact]
    public async Task ScheduleRecordingAsync_throws_when_profile_lacks_dvr_permission()
    {
        var profile = MakeProfile(isAdmin: false, canRecord: false);
        _repo.GetUserProfileAsync(profile.Id).Returns(profile);

        var act = async () => await _manager.ScheduleRecordingAsync(profile.Id, Guid.NewGuid(), "Title", null, false);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*does not have DVR permissions*");
    }

    [Fact]
    public async Task ScheduleRecordingAsync_persists_schedule_when_admin_overrides_permission()
    {
        var profile = MakeProfile(isAdmin: true, canRecord: false);
        _repo.GetUserProfileAsync(profile.Id).Returns(profile);
        _repo.GetAllActiveSchedulesAsync().Returns(new List<IptvRecordingSchedule>());
        _settingsRepo.GetSettingsAsync().Returns(new ServerSetting());

        var channelId = Guid.NewGuid();
        var schedule = await _manager.ScheduleRecordingAsync(profile.Id, channelId, "Title", "prog-1", true, keepMaxEpisodes: 5);

        schedule.Title.Should().Be("Title");
        schedule.ChannelId.Should().Be(channelId);
        schedule.ProgramId.Should().Be("prog-1");
        schedule.IsSeriesRecording.Should().BeTrue();
        schedule.KeepMaxEpisodes.Should().Be(5);
        schedule.UserId.Should().Be(profile.UserId);
        schedule.ProfileId.Should().Be(profile.Id);
        await _repo.Received(1).CreateRecordingScheduleAsync(Arg.Any<IptvRecordingSchedule>());
    }

    [Fact]
    public async Task CanAllocateTunerAsync_true_when_no_tuner_profile()
    {
        var pid = Guid.NewGuid();
        _repo.GetTunerProfileByPlaylistIdAsync(pid).Returns((IptvTunerProfile?)null);

        var ok = await _manager.CanAllocateTunerAsync(pid);

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task CanAllocateTunerAsync_true_when_tuner_has_zero_max_streams()
    {
        var pid = Guid.NewGuid();
        _repo.GetTunerProfileByPlaylistIdAsync(pid).Returns(new IptvTunerProfile { PlaylistId = pid, MaxConcurrentStreams = 0 });

        var ok = await _manager.CanAllocateTunerAsync(pid);

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task CanAllocateTunerAsync_true_when_active_count_below_limit()
    {
        var pid = Guid.NewGuid();
        _repo.GetTunerProfileByPlaylistIdAsync(pid).Returns(new IptvTunerProfile { PlaylistId = pid, MaxConcurrentStreams = 2 });
        _repo.GetActiveRecordingCountForPlaylistAsync(pid).Returns(1);

        var ok = await _manager.CanAllocateTunerAsync(pid);

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task CanAllocateTunerAsync_false_when_active_count_equals_limit()
    {
        var pid = Guid.NewGuid();
        _repo.GetTunerProfileByPlaylistIdAsync(pid).Returns(new IptvTunerProfile { PlaylistId = pid, MaxConcurrentStreams = 2 });
        _repo.GetActiveRecordingCountForPlaylistAsync(pid).Returns(2);

        var ok = await _manager.CanAllocateTunerAsync(pid);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessSchedulesIntoSessionsAsync_creates_session_for_matching_series_program()
    {
        var schedule = MakeSchedule("Severance", isSeries: true, externalChannelId: "ch1");
        var program = new IptvProgramDto
        {
            Id = "prog-1",
            ChannelId = "ch1",
            Title = "Severance S02E01",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(3)
        };

        _repo.GetAllActiveSchedulesAsync().Returns(new List<IptvRecordingSchedule> { schedule });
        _settingsRepo.GetSettingsAsync().Returns(new ServerSetting { DvrPreRollSeconds = 60, DvrPostRollSeconds = 120 });
        _epg.GetProgramsForChannels(Arg.Any<List<string>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<string, List<IptvProgramDto>> { ["ch1"] = new() { program } });
        _repo.SessionExistsForProgramAsync(schedule.Id, program.Id).Returns(false);

        await _manager.ProcessSchedulesIntoSessionsAsync();

        await _repo.Received(1).CreateRecordingSessionAsync(Arg.Is<IptvRecordingSession>(s =>
            s.ScheduleId == schedule.Id &&
            s.Title == program.Title &&
            s.ExternalProgramId == program.Id &&
            s.StartTime == program.StartTime.AddSeconds(-60) &&
            s.EndTime == program.EndTime.AddSeconds(120)));
    }

    [Fact]
    public async Task ProcessSchedulesIntoSessionsAsync_skips_program_when_session_already_exists()
    {
        var schedule = MakeSchedule("Severance", isSeries: true, externalChannelId: "ch1");
        var program = new IptvProgramDto
        {
            Id = "prog-1",
            ChannelId = "ch1",
            Title = "Severance S02E01",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(3)
        };

        _repo.GetAllActiveSchedulesAsync().Returns(new List<IptvRecordingSchedule> { schedule });
        _settingsRepo.GetSettingsAsync().Returns(new ServerSetting());
        _epg.GetProgramsForChannels(Arg.Any<List<string>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<string, List<IptvProgramDto>> { ["ch1"] = new() { program } });
        _repo.SessionExistsForProgramAsync(schedule.Id, program.Id).Returns(true);

        await _manager.ProcessSchedulesIntoSessionsAsync();

        await _repo.DidNotReceive().CreateRecordingSessionAsync(Arg.Any<IptvRecordingSession>());
    }

    [Fact]
    public async Task ProcessSchedulesIntoSessionsAsync_matches_one_off_recording_by_program_id()
    {
        var schedule = MakeSchedule("Different Title", isSeries: false, externalChannelId: "ch1", programId: "prog-7");
        var program = new IptvProgramDto
        {
            Id = "prog-7",
            ChannelId = "ch1",
            Title = "Some Movie",
            StartTime = DateTime.UtcNow.AddHours(1),
            EndTime = DateTime.UtcNow.AddHours(3)
        };

        _repo.GetAllActiveSchedulesAsync().Returns(new List<IptvRecordingSchedule> { schedule });
        _settingsRepo.GetSettingsAsync().Returns(new ServerSetting());
        _epg.GetProgramsForChannels(Arg.Any<List<string>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<string, List<IptvProgramDto>> { ["ch1"] = new() { program } });

        await _manager.ProcessSchedulesIntoSessionsAsync();

        await _repo.Received(1).CreateRecordingSessionAsync(Arg.Any<IptvRecordingSession>());
    }

    [Fact]
    public async Task ProcessSchedulesIntoSessionsAsync_skips_series_program_in_past()
    {
        var schedule = MakeSchedule("Severance", isSeries: true, externalChannelId: "ch1");
        var pastProgram = new IptvProgramDto
        {
            Id = "prog-old",
            ChannelId = "ch1",
            Title = "Severance S01E01",
            StartTime = DateTime.UtcNow.AddHours(-2),
            EndTime = DateTime.UtcNow.AddHours(-1)
        };

        _repo.GetAllActiveSchedulesAsync().Returns(new List<IptvRecordingSchedule> { schedule });
        _settingsRepo.GetSettingsAsync().Returns(new ServerSetting());
        _epg.GetProgramsForChannels(Arg.Any<List<string>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<string, List<IptvProgramDto>> { ["ch1"] = new() { pastProgram } });

        await _manager.ProcessSchedulesIntoSessionsAsync();

        await _repo.DidNotReceive().CreateRecordingSessionAsync(Arg.Any<IptvRecordingSession>());
    }

    [Fact]
    public async Task ProcessSchedulesIntoSessionsAsync_drops_new_session_when_conflict_policy_is_drop_newest()
    {
        var playlistId = Guid.NewGuid();
        var schedule = MakeSchedule("Severance", isSeries: true, externalChannelId: "ch1", playlistId: playlistId);
        var program = new IptvProgramDto
        {
            Id = "prog-1",
            ChannelId = "ch1",
            Title = "Severance S02E01",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(3)
        };

        _repo.GetAllActiveSchedulesAsync().Returns(new List<IptvRecordingSchedule> { schedule });
        _settingsRepo.GetSettingsAsync().Returns(new ServerSetting { DvrConflictPolicy = DvrConflictPolicy.DropNewest });
        _epg.GetProgramsForChannels(Arg.Any<List<string>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<string, List<IptvProgramDto>> { ["ch1"] = new() { program } });
        _repo.GetTunerProfileByPlaylistIdAsync(playlistId).Returns(new IptvTunerProfile { PlaylistId = playlistId, MaxConcurrentStreams = 1 });
        _repo.GetPendingSessionsOverlappingAsync(playlistId, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<IptvRecordingSession>
            {
                new() { Id = Guid.NewGuid(), Title = "Existing", Status = IptvRecordingSessionStatus.Pending }
            });

        await _manager.ProcessSchedulesIntoSessionsAsync();

        await _repo.DidNotReceive().CreateRecordingSessionAsync(Arg.Any<IptvRecordingSession>());
    }

    [Fact]
    public async Task ProcessSchedulesIntoSessionsAsync_drops_oldest_and_creates_new_when_drop_oldest_policy()
    {
        var playlistId = Guid.NewGuid();
        var schedule = MakeSchedule("Severance", isSeries: true, externalChannelId: "ch1", playlistId: playlistId);
        var program = new IptvProgramDto
        {
            Id = "prog-1",
            ChannelId = "ch1",
            Title = "Severance S02E01",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(3)
        };

        var oldestId = Guid.NewGuid();
        _repo.GetAllActiveSchedulesAsync().Returns(new List<IptvRecordingSchedule> { schedule });
        _settingsRepo.GetSettingsAsync().Returns(new ServerSetting { DvrConflictPolicy = DvrConflictPolicy.DropOldest });
        _epg.GetProgramsForChannels(Arg.Any<List<string>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<string, List<IptvProgramDto>> { ["ch1"] = new() { program } });
        _repo.GetTunerProfileByPlaylistIdAsync(playlistId).Returns(new IptvTunerProfile { PlaylistId = playlistId, MaxConcurrentStreams = 1 });
        _repo.GetPendingSessionsOverlappingAsync(playlistId, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<IptvRecordingSession>
            {
                new() { Id = oldestId, Title = "Older", StartTime = DateTime.UtcNow.AddHours(1), Status = IptvRecordingSessionStatus.Pending }
            });

        await _manager.ProcessSchedulesIntoSessionsAsync();

        await _repo.Received(1).DeleteSessionAsync(oldestId);
        await _repo.Received(1).CreateRecordingSessionAsync(Arg.Any<IptvRecordingSession>());
    }

    [Fact]
    public async Task EnforceRetentionPolicyAsync_noop_when_keep_max_episodes_is_zero()
    {
        var schedule = MakeSchedule("Doc", isSeries: true, externalChannelId: "ch1");
        schedule.KeepMaxEpisodes = 0;
        _repo.GetScheduleWithSessionsAsync(schedule.Id).Returns(schedule);

        await _manager.EnforceRetentionPolicyAsync(schedule.Id);

        await _repo.DidNotReceive().DeleteSessionAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task EnforceRetentionPolicyAsync_deletes_oldest_completed_when_count_exceeds_keep()
    {
        var schedule = MakeSchedule("Doc", isSeries: true, externalChannelId: "ch1");
        schedule.KeepMaxEpisodes = 2;
        var newest = new IptvRecordingSession { Id = Guid.NewGuid(), Title = "S2", StartTime = DateTime.UtcNow.AddDays(-1), Status = IptvRecordingSessionStatus.Completed, OutputFilePath = "/tmp/newest.mp4" };
        var middle = new IptvRecordingSession { Id = Guid.NewGuid(), Title = "S1", StartTime = DateTime.UtcNow.AddDays(-2), Status = IptvRecordingSessionStatus.Completed, OutputFilePath = "/tmp/middle.mp4" };
        var oldest = new IptvRecordingSession { Id = Guid.NewGuid(), Title = "S0", StartTime = DateTime.UtcNow.AddDays(-3), Status = IptvRecordingSessionStatus.Completed, OutputFilePath = "/tmp/oldest.mp4" };
        schedule.Sessions = new List<IptvRecordingSession> { middle, newest, oldest };
        _repo.GetScheduleWithSessionsAsync(schedule.Id).Returns(schedule);

        await _manager.EnforceRetentionPolicyAsync(schedule.Id);

        await _repo.Received(1).DeleteSessionAsync(oldest.Id);
        await _repo.DidNotReceive().DeleteSessionAsync(newest.Id);
        await _repo.DidNotReceive().DeleteSessionAsync(middle.Id);
    }

    [Fact]
    public async Task EnforceRetentionPolicyAsync_ignores_incomplete_sessions_for_count()
    {
        var schedule = MakeSchedule("Doc", isSeries: true, externalChannelId: "ch1");
        schedule.KeepMaxEpisodes = 1;
        var inProgress = new IptvRecordingSession { Id = Guid.NewGuid(), Status = IptvRecordingSessionStatus.Recording, OutputFilePath = "/tmp/x.mp4" };
        var completed = new IptvRecordingSession { Id = Guid.NewGuid(), StartTime = DateTime.UtcNow.AddDays(-1), Status = IptvRecordingSessionStatus.Completed, OutputFilePath = "/tmp/done.mp4" };
        schedule.Sessions = new List<IptvRecordingSession> { inProgress, completed };
        _repo.GetScheduleWithSessionsAsync(schedule.Id).Returns(schedule);

        await _manager.EnforceRetentionPolicyAsync(schedule.Id);

        await _repo.DidNotReceive().DeleteSessionAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task DeleteRecordingAsync_returns_silently_when_session_missing()
    {
        _repo.GetSessionByIdAsync(Arg.Any<Guid>()).Returns((IptvRecordingSession?)null);

        await _manager.DeleteRecordingAsync(Guid.NewGuid());

        await _repo.DidNotReceive().DeleteSessionAsync(Arg.Any<Guid>());
        await _repo.DidNotReceive().UpdateSessionStatusAsync(Arg.Any<Guid>(), Arg.Any<IptvRecordingSessionStatus>(), Arg.Any<string?>(), Arg.Any<string?>());
        await _notifier.DidNotReceive().NotifyDvrSessionsUpdatedAsync();
    }

    [Fact]
    public async Task DeleteRecordingAsync_cancels_pending_session_and_notifies()
    {
        var session = new IptvRecordingSession { Id = Guid.NewGuid(), Status = IptvRecordingSessionStatus.Pending };
        _repo.GetSessionByIdAsync(session.Id).Returns(session);

        await _manager.DeleteRecordingAsync(session.Id);

        await _repo.Received(1).UpdateSessionStatusAsync(session.Id, IptvRecordingSessionStatus.Cancelled, Arg.Any<string?>(), Arg.Any<string?>());
        await _repo.DidNotReceive().DeleteSessionAsync(session.Id);
        await _notifier.Received(1).NotifyDvrSessionsUpdatedAsync();
    }

    [Fact]
    public async Task DeleteRecordingAsync_hard_deletes_completed_session()
    {
        var session = new IptvRecordingSession { Id = Guid.NewGuid(), Status = IptvRecordingSessionStatus.Completed };
        _repo.GetSessionByIdAsync(session.Id).Returns(session);

        await _manager.DeleteRecordingAsync(session.Id);

        await _repo.Received(1).DeleteSessionAsync(session.Id);
        await _notifier.Received(1).NotifyDvrSessionsUpdatedAsync();
    }

    [Fact]
    public async Task CancelSeriesAsync_disables_schedule_and_deletes_pending_sessions()
    {
        var scheduleId = Guid.NewGuid();
        var triggerSession = new IptvRecordingSession { Id = Guid.NewGuid(), ScheduleId = scheduleId, Status = IptvRecordingSessionStatus.Pending };
        var pendingA = new IptvRecordingSession { Id = Guid.NewGuid(), ScheduleId = scheduleId, Status = IptvRecordingSessionStatus.Pending };
        var pendingB = new IptvRecordingSession { Id = Guid.NewGuid(), ScheduleId = scheduleId, Status = IptvRecordingSessionStatus.Pending };
        var alreadyDone = new IptvRecordingSession { Id = Guid.NewGuid(), ScheduleId = scheduleId, Status = IptvRecordingSessionStatus.Completed };

        var schedule = new IptvRecordingSchedule
        {
            Id = scheduleId,
            Sessions = new List<IptvRecordingSession> { pendingA, pendingB, alreadyDone }
        };

        _repo.GetSessionByIdAsync(triggerSession.Id).Returns(triggerSession);
        _repo.GetScheduleWithSessionsAsync(scheduleId).Returns(schedule);

        await _manager.CancelSeriesAsync(triggerSession.Id);

        await _repo.Received(1).DisableScheduleAsync(scheduleId);
        await _repo.Received(1).DeleteSessionAsync(pendingA.Id);
        await _repo.Received(1).DeleteSessionAsync(pendingB.Id);
        await _repo.DidNotReceive().DeleteSessionAsync(alreadyDone.Id);
        await _notifier.Received(1).NotifyDvrSessionsUpdatedAsync();
    }

    private static IptvRecordingSchedule MakeSchedule(string title, bool isSeries, string externalChannelId, string? programId = null, Guid? playlistId = null)
    {
        var channel = new IptvChannel
        {
            Id = Guid.NewGuid(),
            ExternalChannelId = externalChannelId,
            Name = externalChannelId,
            StreamUrl = "http://example.com/stream.m3u8",
            PlaylistId = playlistId ?? Guid.NewGuid()
        };

        return new IptvRecordingSchedule
        {
            Id = Guid.NewGuid(),
            Title = title,
            IsSeriesRecording = isSeries,
            ProgramId = programId,
            ChannelId = channel.Id,
            Channel = channel,
            UserId = Guid.NewGuid(),
            ProfileId = Guid.NewGuid()
        };
    }
}
