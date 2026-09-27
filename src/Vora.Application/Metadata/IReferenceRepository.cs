using Vora.Domain.Entities.Media;

namespace Vora.Application.Metadata;

public interface IReferenceRepository
{
    Task<List<Genre>> GetGenresByIdsAsync(IEnumerable<int> genreIds);
    Task<List<Company>> GetCompaniesByTmdbIdsAsync(IEnumerable<int> tmdbIds);
    Task AddCompaniesAsync(IEnumerable<Company> companies);
    Task<List<Country>> GetCountriesByIsoCodesAsync(IEnumerable<string> isoCodes);
    Task AddCountriesAsync(IEnumerable<Country> countries);
    Task<List<Network>> GetNetworksByTmdbIdsAsync(IEnumerable<int> tmdbIds);
    Task AddNetworksAsync(IEnumerable<Network> networks);
}