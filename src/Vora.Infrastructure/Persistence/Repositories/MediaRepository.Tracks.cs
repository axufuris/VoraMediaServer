using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public partial class MediaRepository
{
    public async Task SyncMediaTracksAsync(Guid mediaPartId, List<MediaVideoTrack> incomingVideo, List<MediaAudioTrack> incomingAudio, List<MediaSubtitleTrack> incomingSubtitles)
    {
        var existingVideo = await _context.MediaVideoTracks.Where(t => t.MediaPartId == mediaPartId).ToListAsync();
        var existingAudio = await _context.MediaAudioTracks.Where(t => t.MediaPartId == mediaPartId).ToListAsync();
        var existingSubs = await _context.MediaSubtitleTracks.Where(t => t.MediaPartId == mediaPartId).ToListAsync();

        var inVideoIdx = incomingVideo.Select(v => v.StreamIndex).ToHashSet();
        var inAudioIdx = incomingAudio.Select(a => a.StreamIndex).ToHashSet();
        var inSubIdx = incomingSubtitles.Select(s => s.StreamIndex).ToHashSet();

        _context.MediaVideoTracks.RemoveRange(existingVideo.Where(v => !inVideoIdx.Contains(v.StreamIndex)));
        _context.MediaAudioTracks.RemoveRange(existingAudio.Where(a => !inAudioIdx.Contains(a.StreamIndex)));
        _context.MediaSubtitleTracks.RemoveRange(existingSubs.Where(s => !inSubIdx.Contains(s.StreamIndex)));

        var existingVideoByIdx = existingVideo.ToDictionary(v => v.StreamIndex);
        foreach (var inc in incomingVideo)
        {
            if (existingVideoByIdx.TryGetValue(inc.StreamIndex, out var ex))
            {
                ex.Codec = inc.Codec; ex.Profile = inc.Profile; ex.HdrType = inc.HdrType;
                ex.BitDepth = inc.BitDepth; ex.Bitrate = inc.Bitrate; ex.IsDefault = inc.IsDefault;
            }
            else
            {
                inc.MediaPartId = mediaPartId;
                await _context.MediaVideoTracks.AddAsync(inc);
            }
        }

        var existingAudioByIdx = existingAudio.ToDictionary(a => a.StreamIndex);
        foreach (var inc in incomingAudio)
        {
            if (existingAudioByIdx.TryGetValue(inc.StreamIndex, out var ex))
            {
                ex.Codec = inc.Codec; ex.Language = inc.Language; ex.Channels = inc.Channels;
                ex.Title = inc.Title; ex.IsDefault = inc.IsDefault;
            }
            else
            {
                inc.MediaPartId = mediaPartId;
                await _context.MediaAudioTracks.AddAsync(inc);
            }
        }

        var existingSubsByIdx = existingSubs.ToDictionary(s => s.StreamIndex);
        foreach (var inc in incomingSubtitles)
        {
            if (existingSubsByIdx.TryGetValue(inc.StreamIndex, out var ex))
            {
                ex.Codec = inc.Codec; ex.Language = inc.Language; ex.Title = inc.Title;
                ex.IsDefault = inc.IsDefault; ex.IsForced = inc.IsForced;
            }
            else
            {
                inc.MediaPartId = mediaPartId;
                await _context.MediaSubtitleTracks.AddAsync(inc);
            }
        }

        await _context.SaveChangesAsync();
    }
}
