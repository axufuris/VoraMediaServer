namespace Vora.Application.Auth;

public enum AdminCreateUserOutcome
{
    Created = 0,
    Invalid = 1,
    EmailInUse = 2,
}

public sealed record AdminCreateUserResult(AdminCreateUserOutcome Outcome, Guid? UserId, string? Error)
{
    public static AdminCreateUserResult Created(Guid userId) => new(AdminCreateUserOutcome.Created, userId, null);
    public static AdminCreateUserResult Invalid(string error) => new(AdminCreateUserOutcome.Invalid, null, error);
    public static AdminCreateUserResult EmailInUse { get; } = new(AdminCreateUserOutcome.EmailInUse, null, "An account with that email already exists.");
}
