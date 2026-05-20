using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Iptv.Dtos;
using Vora.Application.Settings;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public interface IDvrManager
{
    Task<IptvRecordingSchedule> ScheduleRecordingAsync(Guid profileId, Guid channelId, string title, string? programId, bool isSeries, int keepMaxEpisodes = 0);
    Task<bool> CanAllocateTunerAsync(Guid playlistId);
    Task MarkSessionFailedAsync(Guid sessionId, string reason);
    Task ProcessSchedulesIntoSessionsAsync();
    Task EnforceRetentionPolicyAsync(Guid scheduleId);
    Task DeleteRecordingAsync(Guid sessionId);
    Task CancelSeriesAsync(Guid sessionId);
}

public class DvrManager : IDvrManager
{
    private const int SessionLookaheadDays = 14;

    private static readonly SemaphoreSlim _tunerLock = new(1, 1);

    private readonly IIptvRepository _repository;
    private readonly IIptvEpgService _epgService;
    private readonly ILogger<DvrManager> _logger;
    private readonly IClientNotifier _notifier;
    private readonly ISystemSettingsRepository _settingsRepo;

    public DvrManager(IIptvRepository repository, IIptvEpgService epgService, ILogger<DvrManager> logger, IClientNotifier notifier, ISystemSettingsRepository settingsRepo)
    {
        _repository = repository;
        _epgService = epgService;
        _logger = logger;
        _notifier = notifier;
        _settingsRepo = settingsRepo;
    }

    public async Task<IptvRecordingSchedule> ScheduleRecordingAsync(Guid profileId, Guid channelId, string title, string? programId, bool isSeries, int keepMaxEpisodes = 0)
    {
        var profile = await _repository.GetUserProfileAsync(profileId);
        if (profile == null)
        {
            throw new UnauthorizedAccessException("Profile not found.");
        }

        if (!profile.IsAdmin && !profile.CanRecordLiveTv)
        {
            throw new UnauthorizedAccessException("This profile does not have DVR permissions.");
        }

        var schedule = new IptvRecordingSchedule
        {
            UserId = profile.UserId,
            ProfileId = profileId,
            ChannelId = channelId,
            Title = title,
            ProgramId = programId,
            IsSeriesRecording = isSeries,
            KeepMaxEpisodes = keepMaxEpisodes
        };

        await _repository.CreateRecordingScheduleAsync(schedule);
        await ProcessSchedulesIntoSessionsAsync();

        _logger.LogInformation("DVR Schedule created: '{Title}' for Profile {ProfileId} (Parent User: {UserId}).", title, profileId, profile.UserId);
        return schedule;
    }

    public async Task<bool> CanAllocateTunerAsync(Guid playlistId)
    {
        await _tunerLock.WaitAsync();
        try
        {
            var tunerProfile = await _repository.GetTunerProfileByPlaylistIdAsync(playlistId);

            if (tunerProfile == null || tunerProfile.MaxConcurrentStreams <= 0)
            {
                return true;
            }

            var activeRecordings = await _repository.GetActiveRecordingCountForPlaylistAsync(playlistId);

            if (activeRecordings >= tunerProfile.MaxConcurrentStreams)
            {
                _logger.LogWarning("Tuner allocation failed for Playlist {PlaylistId}. Limit of {Limit} reached.", playlistId, tunerProfile.MaxConcurrentStreams);
                return false;
            }

            return true;
        }
        finally
        {
            _tunerLock.Release();
        }
    }

    public Task MarkSessionFailedAsync(Guid sessionId, string reason) =>
        _repository.MarkSessionFailedAsync(sessionId, reason);

    public async Task ProcessSchedulesIntoSessionsAsync()
    {
        _logger.LogInformation("[DVR] Starting EPG-to-Session translation.");

        var schedules = await _repository.GetAllActiveSchedulesAsync();
        if (!schedules.Any()) return;

        var settings = await _settingsRepo.GetSettingsAsync();

        var schedulesByChannel = schedules.GroupBy(s => s.Channel.ExternalChannelId).ToList();

        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddDays(SessionLookaheadDays);
        var requestedChannelIds = schedulesByChannel.Select(g => g.Key).ToList();

        var epgData = _epgService.GetProgramsForChannels(requestedChannelIds, startTime, endTime);

        var newSessionsCreated = 0;

        foreach (var channelGroup in schedulesByChannel)
        {
            if (!epgData.TryGetValue(channelGroup.Key, out var programs)) continue;

            foreach (var schedule in channelGroup)
            {
                foreach (var program in programs)
                {
                    if (!IsScheduleMatch(schedule, program)) continue;

                    var exists = await _repository.SessionExistsForProgramAsync(schedule.Id, program.Id);
                    if (exists) continue;

                    var created = await CreateSessionForProgramAsync(schedule, program, settings);
                    if (created) newSessionsCreated++;
                }
            }
        }

        _logger.LogInformation("[DVR] Translation complete. Created {NewSessionCount} new recording sessions.", newSessionsCreated);
    }

    public async Task EnforceRetentionPolicyAsync(Guid scheduleId)
    {
        var schedule = await _repository.GetScheduleWithSessionsAsync(scheduleId);
        if (schedule == null || schedule.KeepMaxEpisodes <= 0) return;

        var completedSessions = schedule.Sessions
            .Where(s => s.Status == IptvRecordingSessionStatus.Completed && !string.IsNullOrEmpty(s.OutputFilePath))
            .OrderByDescending(s => s.StartTime)
            .ToList();

        if (completedSessions.Count <= schedule.KeepMaxEpisodes) return;

        var toDelete = completedSessions.Skip(schedule.KeepMaxEpisodes);
        foreach (var session in toDelete)
        {
            DeleteOutputFileIfExists(session);
            await _repository.DeleteSessionAsync(session.Id);
            _logger.LogInformation("[DVR] Retention Policy: Deleted old episode '{Title}'.", session.Title);
        }
    }

    public async Task DeleteRecordingAsync(Guid sessionId)
    {
        var session = await _repository.GetSessionByIdAsync(sessionId);
        if (session == null) return;

        DeleteOutputFileIfExists(session);

        if (session.Status == IptvRecordingSessionStatus.Pending)
        {
            await _repository.UpdateSessionStatusAsync(sessionId, IptvRecordingSessionStatus.Cancelled, errorMessage: "Manually cancelled by user.");
        }
        else
        {
            await _repository.DeleteSessionAsync(sessionId);
        }

        await _notifier.NotifyDvrSessionsUpdatedAsync();
    }

    public async Task CancelSeriesAsync(Guid sessionId)
    {
        var session = await _repository.GetSessionByIdAsync(sessionId);
        if (session == null) return;

        await _repository.DisableScheduleAsync(session.ScheduleId);

        var scheduleWithSessions = await _repository.GetScheduleWithSessionsAsync(session.ScheduleId);
        if (scheduleWithSessions != null)
        {
            var pendingSessions = scheduleWithSessions.Sessions.Where(s => s.Status == IptvRecordingSessionStatus.Pending).ToList();
            foreach (var pending in pendingSessions)
            {
                await _repository.DeleteSessionAsync(pending.Id);
            }
        }

        await _notifier.NotifyDvrSessionsUpdatedAsync();
    }

    private async Task<bool> CreateSessionForProgramAsync(IptvRecordingSchedule schedule, IptvProgramDto program, Vora.Domain.Entities.Settings.ServerSetting settings)
    {
        var startTime = program.StartTime.AddSeconds(-settings.DvrPreRollSeconds);
        var endTime = program.EndTime.AddSeconds(settings.DvrPostRollSeconds);

        var playlistId = schedule.Channel?.PlaylistId;
        if (playlistId.HasValue && settings.DvrConflictPolicy != DvrConflictPolicy.AlwaysRecord)
        {
            var tunerProfile = await _repository.GetTunerProfileByPlaylistIdAsync(playlistId.Value);
            var maxStreams = tunerProfile?.MaxConcurrentStreams ?? 0;
            if (maxStreams > 0)
            {
                var overlapping = await _repository.GetPendingSessionsOverlappingAsync(playlistId.Value, startTime, endTime);
                if (overlapping.Count >= maxStreams)
                {
                    if (settings.DvrConflictPolicy == DvrConflictPolicy.DropNewest)
                    {
                        _logger.LogWarning("[DVR] Skipped session for '{Title}' — tuner limit reached on playlist, DropNewest policy.", program.Title);
                        return false;
                    }
                    if (settings.DvrConflictPolicy == DvrConflictPolicy.DropOldest)
                    {
                        var oldest = overlapping.OrderBy(s => s.StartTime).First();
                        await _repository.DeleteSessionAsync(oldest.Id);
                        _logger.LogWarning("[DVR] Cancelled conflicting session '{OldTitle}' to make room for '{NewTitle}' (DropOldest policy).", oldest.Title, program.Title);
                    }
                }
            }
        }

        var session = new IptvRecordingSession
        {
            ScheduleId = schedule.Id,
            Title = program.Title,
            EpisodeTitle = program.Description,
            SeasonNumber = program.SeasonNumber,
            EpisodeNumber = program.EpisodeNumber,
            StartTime = startTime,
            EndTime = endTime,
            Status = IptvRecordingSessionStatus.Pending,
            ExternalProgramId = program.Id
        };

        await _repository.CreateRecordingSessionAsync(session);
        _logger.LogInformation("[DVR] Queued session for '{Title}' starting at {StartTime}.", session.Title, session.StartTime.ToLocalTime());
        return true;
    }

    private static bool IsScheduleMatch(IptvRecordingSchedule schedule, IptvProgramDto program)
    {
        if (!string.IsNullOrEmpty(schedule.ProgramId) && schedule.ProgramId == program.Id)
        {
            return true;
        }

        if (schedule.IsSeriesRecording &&
            program.Title.Contains(schedule.Title, StringComparison.OrdinalIgnoreCase) &&
            program.StartTime > DateTime.UtcNow)
        {
            return true;
        }

        return false;
    }

    private void DeleteOutputFileIfExists(IptvRecordingSession session)
    {
        if (string.IsNullOrWhiteSpace(session.OutputFilePath) || !File.Exists(session.OutputFilePath)) return;

        try
        {
            File.Delete(session.OutputFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete DVR file: {FilePath}.", session.OutputFilePath);
        }
    }
}
