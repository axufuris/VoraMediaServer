namespace Vora.Plugins.Dtos;

public class LyricsResult
{
    public string? PlainLyrics { get; init; }
    public string? SyncedLyrics { get; init; }
    public bool IsSynced => !string.IsNullOrWhiteSpace(SyncedLyrics);
    public bool HasAnyLyrics => !string.IsNullOrWhiteSpace(PlainLyrics) || !string.IsNullOrWhiteSpace(SyncedLyrics);
    public required string ProviderName { get; init; }
    public string? SourceUrl { get; init; }
}
