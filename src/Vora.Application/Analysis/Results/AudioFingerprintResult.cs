namespace Vora.Application.Analysis.Results;

public class AudioFingerprintResult
{
    public uint[] Points { get; set; } = Array.Empty<uint>();
    public double PointDurationSeconds { get; set; }
}
