using Microsoft.EntityFrameworkCore;
using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

public class IptvRepository : IIptvRepository
{
    private readonly VoraDbContext _context;

    public IptvRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<List<IptvPlaylist>> GetActivePlaylistsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.IptvPlaylists
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<HashSet<string>> GetActiveChannelIdsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.IptvChannels
            .AsNoTracking()
            .Select(c => c.ExternalChannelId)
            .ToHashSetAsync(cancellationToken);
    }

    public async Task<List<IptvPlaylist>> GetAllPlaylistsAsync(IptvChannelKind? kind = null)
    {
        var query = _context.IptvPlaylists
            .AsNoTracking()
            .Include(p => p.Channels)
            .Include(p => p.TunerProfile)
            .AsQueryable();

        if (kind.HasValue)
        {
            var k = kind.Value;
            query = query.Where(p => p.DefaultChannelKind == k);
        }

        return await query.ToListAsync();
    }

    public async Task<IptvPlaylist?> GetPlaylistByIdAsync(Guid id)
    {
        return await _context.IptvPlaylists
            .Include(p => p.Channels)
            .Include(p => p.TunerProfile)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<IptvChannel>> GetActiveChannelsAsync(CancellationToken cancellationToken)
    {
        return await _context.IptvChannels
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task AddPlaylistAsync(IptvPlaylist playlist)
    {
        await _context.IptvPlaylists.AddAsync(playlist);
        await _context.SaveChangesAsync();
    }

    public async Task UpdatePlaylistAsync(IptvPlaylist playlist)
    {
        if (_context.Entry(playlist).State == EntityState.Detached)
        {
            _context.IptvPlaylists.Attach(playlist);
            _context.Entry(playlist).State = EntityState.Modified;
        }
        await _context.SaveChangesAsync();
    }

    public async Task DeletePlaylistAsync(Guid id)
    {
        var playlist = await _context.IptvPlaylists.FindAsync(id);
        if (playlist != null)
        {
            _context.IptvPlaylists.Remove(playlist);
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateChannelsAsync(Guid playlistId, List<IptvChannel> newChannels)
    {
        var existingChannels = await _context.IptvChannels.Where(c => c.PlaylistId == playlistId).ToListAsync();

        var existingDict = new Dictionary<string, IptvChannel>(StringComparer.OrdinalIgnoreCase);
        foreach (var ch in existingChannels)
        {
            if (existingDict.ContainsKey(ch.ExternalChannelId))
            {
                _context.IptvChannels.Remove(ch);
            }
            else
            {
                existingDict[ch.ExternalChannelId] = ch;
            }
        }

        var seenIncoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var newChan in newChannels)
        {
            if (!seenIncoming.Add(newChan.ExternalChannelId)) continue;

            if (existingDict.TryGetValue(newChan.ExternalChannelId, out var existing))
            {
                existing.Name = newChan.Name;
                existing.LogoUrl = newChan.LogoUrl;
                existing.GroupTitle = newChan.GroupTitle;
                existing.StreamUrl = newChan.StreamUrl;
                existing.Resolution = newChan.Resolution;
                existing.CountryCode = newChan.CountryCode;

                if (!existing.KindOverriddenByAdmin)
                {
                    existing.Kind = newChan.Kind;
                }

                existingDict.Remove(newChan.ExternalChannelId);
            }
            else
            {
                await _context.IptvChannels.AddAsync(newChan);
            }
        }

        if (existingDict.Count > 0)
        {
            _context.IptvChannels.RemoveRange(existingDict.Values);
        }

        await _context.SaveChangesAsync();
    }

    public async Task ToggleChannelVisibilityAsync(Guid channelId)
    {
        var channel = await _context.IptvChannels.FindAsync(channelId);
        if (channel != null)
        {
            channel.IsHiddenByAdmin = !channel.IsHiddenByAdmin;
            await _context.SaveChangesAsync();
        }
    }

    public async Task SetChannelKindAsync(Guid channelId, IptvChannelKind kind)
    {
        var channel = await _context.IptvChannels.FindAsync(channelId);
        if (channel != null)
        {
            channel.Kind = kind;
            channel.KindOverriddenByAdmin = true;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<IptvEpgSource>> GetActiveEpgSourcesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.IptvEpgSources
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Priority)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<IptvEpgSource>> GetAllEpgSourcesAsync()
    {
        return await _context.IptvEpgSources
            .AsNoTracking()
            .OrderBy(s => s.Priority)
            .ToListAsync();
    }

    public async Task<IptvEpgSource?> GetEpgSourceByIdAsync(Guid id)
    {
        return await _context.IptvEpgSources.FindAsync(id);
    }

    public async Task AddEpgSourceAsync(IptvEpgSource source)
    {
        await _context.IptvEpgSources.AddAsync(source);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateEpgSourceAsync(IptvEpgSource source)
    {
        _context.IptvEpgSources.Update(source);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteEpgSourceAsync(Guid id)
    {
        var source = await _context.IptvEpgSources.FindAsync(id);
        if (source != null)
        {
            _context.IptvEpgSources.Remove(source);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IptvRecordingSchedule> CreateRecordingScheduleAsync(IptvRecordingSchedule schedule)
    {
        await _context.IptvRecordingSchedules.AddAsync(schedule);
        await _context.SaveChangesAsync();
        return schedule;
    }

    public async Task<IptvTunerProfile?> GetTunerProfileByPlaylistIdAsync(Guid playlistId)
    {
        return await _context.IptvTunerProfiles.AsNoTracking().FirstOrDefaultAsync(t => t.PlaylistId == playlistId);
    }

    public async Task<int> GetActiveRecordingCountForPlaylistAsync(Guid playlistId)
    {
        return await _context.IptvRecordingSessions
            .CountAsync(s => s.Status == IptvRecordingSessionStatus.Recording && s.Schedule.Channel.PlaylistId == playlistId);
    }

    public async Task<List<IptvRecordingSession>> GetPendingSessionsOverlappingAsync(Guid playlistId, DateTime windowStart, DateTime windowEnd)
    {
        return await _context.IptvRecordingSessions
            .Include(s => s.Schedule)
                .ThenInclude(sc => sc.Channel)
            .Where(s => (s.Status == IptvRecordingSessionStatus.Pending || s.Status == IptvRecordingSessionStatus.Recording)
                && s.Schedule.Channel.PlaylistId == playlistId
                && s.StartTime < windowEnd
                && s.EndTime > windowStart)
            .ToListAsync();
    }

    public async Task MarkSessionFailedAsync(Guid sessionId, string reason)
    {
        var session = await _context.IptvRecordingSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Status = IptvRecordingSessionStatus.Failed;
            session.ErrorMessage = reason;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateSessionStatusAsync(Guid sessionId, IptvRecordingSessionStatus status, string? outputPath = null, string? errorMessage = null)
    {
        var session = await _context.IptvRecordingSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Status = status;
            if (outputPath != null) session.OutputFilePath = outputPath;
            if (errorMessage != null) session.ErrorMessage = errorMessage;

            await _context.SaveChangesAsync();
        }
    }

    public async Task<IptvRecordingSession?> GetSessionByIdAsync(Guid sessionId)
    {
        return await _context.IptvRecordingSessions
            .Include(s => s.Schedule)
            .ThenInclude(s => s.Channel)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
    }

    public async Task<List<IptvRecordingSession>> GetPendingSessionsToStartAsync(DateTime checkTime)
    {
        return await _context.IptvRecordingSessions
            .Include(s => s.Schedule)
                .ThenInclude(schedule => schedule.Channel)
            .Where(s => s.Status == IptvRecordingSessionStatus.Pending && s.StartTime <= checkTime)
            .ToListAsync();
    }

    public async Task<List<IptvRecordingSession>> GetActiveSessionsToStopAsync(DateTime checkTime)
    {
        return await _context.IptvRecordingSessions
            .Where(s => s.Status == IptvRecordingSessionStatus.Recording && s.EndTime <= checkTime)
            .ToListAsync();
    }

    public async Task<List<IptvRecordingSchedule>> GetAllActiveSchedulesAsync()
    {
        return await _context.IptvRecordingSchedules
            .AsNoTracking()
            .Include(s => s.Channel)
            .Where(s => s.IsActive)
            .ToListAsync();
    }

    public async Task<bool> SessionExistsForProgramAsync(Guid scheduleId, string externalProgramId)
    {
        return await _context.IptvRecordingSessions
            .AnyAsync(s => s.ScheduleId == scheduleId && s.ExternalProgramId == externalProgramId);
    }

    public async Task CreateRecordingSessionAsync(IptvRecordingSession session)
    {
        await _context.IptvRecordingSessions.AddAsync(session);
        await _context.SaveChangesAsync();
    }

    public async Task<List<IptvRecordingSession>> GetSessionsForProfileAsync(Guid profileId)
    {
        return await _context.IptvRecordingSessions
            .AsNoTracking()
            .Include(s => s.Schedule)
            .ThenInclude(schedule => schedule.Channel)
            .Where(s => s.Schedule.ProfileId == profileId)
            .ToListAsync();
    }

    public async Task<User> GetUserWithQuotaAsync(Guid userId)
    {
        return await _context.Users.FirstAsync(u => u.Id == userId);
    }

    public async Task<long> GetDvrUsageBytesAsync(Guid userId)
    {
        return await _context.IptvRecordingSessions
            .AsNoTracking()
            .Where(s => s.Schedule.UserId == userId && s.Status == IptvRecordingSessionStatus.Completed && s.FileSizeBytes > 0)
            .SumAsync(s => (long?)s.FileSizeBytes) ?? 0;
    }

    public async Task<long> GetDvrTotalUsageBytesAsync()
    {
        var sum = await _context.IptvRecordingSessions
            .AsNoTracking()
            .Where(s => s.Status == IptvRecordingSessionStatus.Completed && s.FileSizeBytes > 0)
            .SumAsync(s => (long?)s.FileSizeBytes) ?? 0;
        return sum;
    }

    public async Task<IptvRecordingSchedule?> GetScheduleWithSessionsAsync(Guid scheduleId)
    {
        return await _context.IptvRecordingSchedules
            .Include(s => s.Sessions)
            .FirstOrDefaultAsync(s => s.Id == scheduleId);
    }

    public async Task DeleteSessionAsync(Guid sessionId)
    {
        var session = await _context.IptvRecordingSessions.FindAsync(sessionId);
        if (session != null)
        {
            if (!string.IsNullOrEmpty(session.OutputFilePath) && File.Exists(session.OutputFilePath))
            {
                File.Delete(session.OutputFilePath);
            }
            _context.IptvRecordingSessions.Remove(session);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IptvChannel?> GetChannelByIdAsync(Guid id)
    {
        return await _context.IptvChannels.FindAsync(id);
    }

    public async Task<ServerSetting> GetServerSettingsAsync()
    {
        return await _context.ServerSettings.FirstOrDefaultAsync() ?? new ServerSetting();
    }

    public async Task<UserProfile?> GetUserProfileAsync(Guid profileId)
    {
        return await _context.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == profileId);
    }

    public async Task<List<IptvRecordingSession>> GetCompletedRawSessionsAsync()
    {
        return await _context.IptvRecordingSessions
            .Where(s => s.Status == IptvRecordingSessionStatus.Completed && s.OutputFilePath != null && s.OutputFilePath.EndsWith(".ts"))
            .ToListAsync();
    }

    public async Task UpdateSessionAsync(IptvRecordingSession session)
    {
        _context.IptvRecordingSessions.Update(session);
        await _context.SaveChangesAsync();
    }

    public async Task DisableScheduleAsync(Guid scheduleId)
    {
        var schedule = await _context.IptvRecordingSchedules.FindAsync(scheduleId);
        if (schedule != null)
        {
            schedule.IsActive = false;
            await _context.SaveChangesAsync();
        }
    }
}
