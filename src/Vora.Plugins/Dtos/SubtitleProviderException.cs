namespace Vora.Plugins.Dtos;

// Raised when a subtitle provider refuses for a reason the VIEWER needs to see —
// a spent download quota above all. A null return says only "no subtitle", which
// would surface as a generic failure and leave someone retrying against a wall
// that will not move until their quota resets.
public class SubtitleProviderException : Exception
{
    public SubtitleProviderException(string message, bool isQuotaExhausted = false, DateTime? retryAfterUtc = null)
        : base(message)
    {
        IsQuotaExhausted = isQuotaExhausted;
        RetryAfterUtc = retryAfterUtc;
    }

    public bool IsQuotaExhausted { get; }
    public DateTime? RetryAfterUtc { get; }
}
