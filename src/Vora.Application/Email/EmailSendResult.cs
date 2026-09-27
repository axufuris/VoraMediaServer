namespace Vora.Application.Email;

public enum EmailSendOutcome
{
    Queued,
    Sent,
    Skipped,
    Failed
}

public class EmailSendResult
{
    public required EmailSendOutcome Outcome { get; init; }
    public Guid? LogId { get; init; }
    public string? ErrorMessage { get; init; }

    public static EmailSendResult Queued(Guid logId) => new() { Outcome = EmailSendOutcome.Queued, LogId = logId };
    public static EmailSendResult Sent(Guid logId) => new() { Outcome = EmailSendOutcome.Sent, LogId = logId };
    public static EmailSendResult Skipped(string reason) => new() { Outcome = EmailSendOutcome.Skipped, ErrorMessage = reason };
    public static EmailSendResult Failed(string error, Guid? logId = null) => new() { Outcome = EmailSendOutcome.Failed, LogId = logId, ErrorMessage = error };
}
