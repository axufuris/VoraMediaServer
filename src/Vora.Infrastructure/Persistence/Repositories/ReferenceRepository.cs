using Microsoft.EntityFrameworkCore;
using Vora.Application.Metadata;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public class ReferenceRepository : IReferenceRepository
{
    private readonly VoraDbContext _context;

    public ReferenceRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<List<Genre>> GetGenresByIdsAsync(IEnumerable<int> genreIds) =>
        await _context.Genres.Where(g => genreIds.Contains(g.Id)).ToListAsync();

    public async Task<List<Company>> GetCompaniesByTmdbIdsAsync(IEnumerable<int> tmdbIds) =>
        await _context.Companies.Where(c => tmdbIds.Contains(c.Id)).ToListAsync();

    public async Task AddCompaniesAsync(IEnumerable<Company> companies) =>
        await _context.Companies.AddRangeAsync(companies);

    public async Task<List<Country>> GetCountriesByIsoCodesAsync(IEnumerable<string> isoCodes) =>
        await _context.Countries.Where(c => isoCodes.Contains(c.Iso3166_1)).ToListAsync();

    public async Task AddCountriesAsync(IEnumerable<Country> countries) =>
        await _context.Countries.AddRangeAsync(countries);

    public async Task<List<Network>> GetNetworksByTmdbIdsAsync(IEnumerable<int> tmdbIds) =>
        await _context.Networks.Where(n => tmdbIds.Contains(n.Id)).ToListAsync();

    public async Task AddNetworksAsync(IEnumerable<Network> networks) =>
        await _context.Networks.AddRangeAsync(networks);
}