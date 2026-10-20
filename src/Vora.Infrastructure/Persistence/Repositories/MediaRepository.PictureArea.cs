using Microsoft.EntityFrameworkCore;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public partial class MediaRepository
{
    public Task<List<PictureAreaTarget>> GetPictureAreaTargetsForItemAsync(Guid mediaItemId) =>
        PictureAreaTargetsAsync(_context.MediaParts.Where(p => p.MediaItemId == mediaItemId));

    public Task<List<PictureAreaTarget>> GetPictureAreaTargetsForLibraryAsync(Guid libraryId) =>
        PictureAreaTargetsAsync(_context.MediaParts.Where(p =>
            p.MediaItem != null &&
            p.MediaItem.MissingSince == null &&
            ((p.MediaItem is Movie && p.MediaItem.LibraryId == libraryId) ||
             (p.MediaItem is Episode && ((Episode)p.MediaItem).Season.TvShow.LibraryId == libraryId))));

    public async Task SavePictureCropAsync(Guid videoTrackId, PictureCrop crop)
    {
        var track = await _context.MediaVideoTracks.FirstOrDefaultAsync(t => t.Id == videoTrackId);
        if (track == null) return;
        crop.ApplyTo(track);
        await _context.SaveChangesAsync();
    }

    private async Task<List<PictureAreaTarget>> PictureAreaTargetsAsync(IQueryable<MediaPart> parts)
    {
        var rows = await parts
            .AsNoTracking()
            .Where(p => p.Duration != null && p.VideoTracks.Any(v => v.CropTop == null))
            .Select(p => new
            {
                p.MediaItemId,
                p.FilePath,
                p.Duration,
                VideoTracks = p.VideoTracks.Select(v => new MediaVideoTrack
                {
                    Id = v.Id,
                    StreamIndex = v.StreamIndex,
                    Codec = v.Codec,
                    IsDefault = v.IsDefault,
                    Width = v.Width,
                    Height = v.Height,
                    CropTop = v.CropTop,
                }).ToList(),
            })
            .ToListAsync();

        var targets = new List<PictureAreaTarget>();
        foreach (var row in rows)
        {
            if (CoverImageTracks.MainVideoTrack(row.VideoTracks) is not { CropTop: null, Width: int width and > 0, Height: int height and > 0 } track) continue;
            if (row.Duration is not { TotalSeconds: > 0 } duration || row.MediaItemId is not Guid mediaItemId) continue;
            targets.Add(new PictureAreaTarget(track.Id, mediaItemId, row.FilePath, track.StreamIndex, width, height, duration.TotalSeconds));
        }
        return targets;
    }
}
