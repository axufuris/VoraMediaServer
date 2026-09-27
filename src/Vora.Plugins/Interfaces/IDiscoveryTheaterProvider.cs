using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IDiscoveryTheaterProvider : IVoraPlugin
{
    Task<IEnumerable<TheaterDto>> GetShowtimesAsync(string movieTitle, string location, DateTime date, int? maxTheaters = null);
    Task<bool> IsAutoLoadEnabledAsync();
}
