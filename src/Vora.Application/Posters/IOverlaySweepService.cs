namespace Vora.Application.Posters;

public interface IOverlaySweepService
{
    void SweepPhysicalOverlays(IEnumerable<string?> urls);
}
