namespace Vora.Api.Extensions;

public static class VoraRateLimitPolicies
{
    public const string AuthStrict = "auth-strict";
    public const string AuthBurst = "auth-burst";
    public const string AuthPoll = "auth-poll";
    public const string WebhookReceive = "webhook-receive";
}
