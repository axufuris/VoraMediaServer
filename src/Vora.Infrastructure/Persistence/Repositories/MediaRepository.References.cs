using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public partial class MediaRepository
{
    public async Task SetMediaCompaniesAsync(Guid mediaItemId, IEnumerable<int> companyIds)
    {
        var item = await _context.MediaItems.Include(m => m.ProductionCompanies).FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (item == null) return;

        var ids = companyIds.ToList();
        var companies = await _context.Set<Company>().Where(c => ids.Contains(c.Id)).ToListAsync();
        item.ProductionCompanies.Clear();
        foreach (var c in companies) item.ProductionCompanies.Add(c);

        await _context.SaveChangesAsync();
    }

    public async Task SetMediaCountriesAsync(Guid mediaItemId, IEnumerable<string> countryIsoCodes)
    {
        var item = await _context.MediaItems.Include(m => m.OriginCountries).FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (item == null) return;

        var codes = countryIsoCodes.ToList();
        var countries = await _context.Set<Country>().Where(c => codes.Contains(c.Iso3166_1)).ToListAsync();
        item.OriginCountries.Clear();
        foreach (var c in countries) item.OriginCountries.Add(c);

        await _context.SaveChangesAsync();
    }

    public async Task SetMediaGenresAsync(Guid mediaItemId, IEnumerable<int> genreIds)
    {
        var item = await _context.MediaItems.Include(m => m.Genres).FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (item == null) return;

        var ids = genreIds.ToList();
        var genres = await _context.Set<Genre>().Where(g => ids.Contains(g.Id)).ToListAsync();
        item.Genres.Clear();
        foreach (var g in genres) item.Genres.Add(g);

        await _context.SaveChangesAsync();
    }

    public async Task SetTvNetworksAsync(Guid tvShowId, IEnumerable<int> networkIds)
    {
        var show = await _context.Set<TvShow>().Include(t => t.Networks).FirstOrDefaultAsync(t => t.Id == tvShowId);
        if (show == null) return;

        var ids = networkIds.ToList();
        var networks = await _context.Set<Network>().Where(n => ids.Contains(n.Id)).ToListAsync();
        show.Networks.Clear();
        foreach (var n in networks) show.Networks.Add(n);

        await _context.SaveChangesAsync();
    }
}
