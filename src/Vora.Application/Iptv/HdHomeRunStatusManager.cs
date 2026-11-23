using Vora.Application.Iptv.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public interface IHdHomeRunStatusManager
{
    Task<HdHomeRunStatusVM?> GetAsync(Guid playlistId, CancellationToken cancellationToken = default);
}

public sealed class HdHomeRunStatusManager : IHdHomeRunStatusManager
{
    private const double HertzPerMegahertz = 1_000_000d;

    private readonly IIptvRepository _repository;
    private readonly IHdHomeRunClient _client;
    private readonly ITunerRegistry _tunerRegistry;

    public HdHomeRunStatusManager(IIptvRepository repository, IHdHomeRunClient client, ITunerRegistry tunerRegistry)
    {
        _repository = repository;
        _client = client;
        _tunerRegistry = tunerRegistry;
    }

    public async Task<HdHomeRunStatusVM?> GetAsync(Guid playlistId, CancellationToken cancellationToken = default)
    {
        var playlist = await _repository.GetPlaylistByIdAsync(playlistId);
        if (playlist is not { SourceType: IptvSourceType.HdHomeRun }) return null;

        var voraStreams = _tunerRegistry.ActiveCount(playlist.Id);
        try
        {
            var status = await _client.GetStatusAsync(playlist, cancellationToken);
            var tuners = status.Tuners.Select(Map).ToList();
            return new HdHomeRunStatusVM
            {
                Succeeded = true,
                DeviceName = status.Device?.Name ?? playlist.Name,
                Model = status.Device?.Model,
                FirmwareVersion = status.Device?.FirmwareVersion,
                DeviceId = status.Device?.DeviceId,
                TunerCount = status.Device?.TunerCount ?? tuners.Count,
                TunersInUse = tuners.Count(t => t.InUse),
                VoraStreams = voraStreams,
                Tuners = tuners
            };
        }
        catch (IptvSourceException ex)
        {
            return new HdHomeRunStatusVM { Succeeded = false, Error = ex.Message, DeviceName = playlist.Name, VoraStreams = voraStreams };
        }
    }

    private static HdHomeRunTunerVM Map(HdHomeRunTuner tuner) => new()
    {
        Index = tuner.Index,
        InUse = tuner.InUse,
        ChannelNumber = tuner.ChannelNumber,
        ChannelName = tuner.ChannelName,
        FrequencyMhz = tuner.FrequencyHz is { } hertz ? Math.Round(hertz / HertzPerMegahertz, 3) : null,
        SignalStrengthPercent = tuner.SignalStrengthPercent,
        SignalQualityPercent = tuner.SignalQualityPercent,
        SymbolQualityPercent = tuner.SymbolQualityPercent,
        BitsPerSecond = tuner.BitsPerSecond,
        TargetAddress = tuner.TargetAddress
    };
}
