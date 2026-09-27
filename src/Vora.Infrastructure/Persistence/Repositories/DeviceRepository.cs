using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Devices;
using Vora.Domain.Entities.Users;

namespace Vora.Infrastructure.Persistence.Repositories;

public class DeviceRepository(VoraDbContext context) : IDeviceRepository
{
    public Task<List<T>> GetProjectedDevicesAsync<T>(Expression<Func<ClientDevice, T>> projection) =>
        context.ClientDevices
            .AsNoTracking()
            .OrderByDescending(d => d.LastConnectedAt)
            .Select(projection)
            .ToListAsync();

    public Task<ClientDevice?> GetDeviceByIdAsync(Guid id) =>
        context.ClientDevices.FindAsync(id).AsTask();

    public async Task UpdateDeviceAsync(ClientDevice device)
    {
        context.ClientDevices.Update(device);
        await context.SaveChangesAsync();
    }

    public async Task DeleteDeviceAsync(ClientDevice device)
    {
        context.ClientDevices.Remove(device);
        await context.SaveChangesAsync();
    }

    public async Task UpsertDeviceCapabilitiesAsync(ClientDevice device)
    {
        var existing = await context.ClientDevices.FirstOrDefaultAsync(d => d.DeviceId == device.DeviceId);

        if (existing == null)
        {
            try
            {
                context.ClientDevices.Add(device);
                await context.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException)
            {
                context.ChangeTracker.Clear();
                existing = await context.ClientDevices.FirstOrDefaultAsync(d => d.DeviceId == device.DeviceId);
                if (existing == null)
                {
                    throw;
                }
            }
        }

        ApplyCapabilities(existing, device);
        context.ClientDevices.Update(existing);
        await context.SaveChangesAsync();
    }

    private static void ApplyCapabilities(ClientDevice target, ClientDevice source)
    {
        target.SupportedVideoCodecs = source.SupportedVideoCodecs;
        target.SupportedAudioCodecs = source.SupportedAudioCodecs;
        target.SupportedContainers = source.SupportedContainers;
        target.MaxAudioChannels = source.MaxAudioChannels;
        target.SupportedHdrFormats = source.SupportedHdrFormats;
        target.MaxVideoBitDepth = source.MaxVideoBitDepth;
        target.LastConnectedAt = source.LastConnectedAt;
    }
}
