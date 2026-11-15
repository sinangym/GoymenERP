namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>
/// The operator terminal of <c>GoymenERP.Api identity bootstrap-founder</c> (spec ID-2 "Kurucu bootstrap komutu").
/// A password is only ever read through <see cref="ReadNewPassword"/> (no echo); it is never taken from an argument, an
/// environment variable or a file.
/// </summary>
public interface IFounderBootstrapConsole
{
    /// <summary><see langword="false"/> when stdin is redirected (<c>cmd &lt; file</c> or a pipe): the command refuses.</summary>
    bool IsInteractive { get; }

    string? ReadUserName();

    string? ReadDisplayName();

    /// <summary>Reads the new password without echoing it.</summary>
    string? ReadNewPassword();

    /// <summary>Reads the repeated new password without echoing it.</summary>
    string? ReadNewPasswordConfirmation();
}

public enum FounderBootstrapStatus
{
    Completed = 1,

    /// <summary>stdin is not a terminal; nothing was read or written.</summary>
    NotInteractive = 2,

    /// <summary><c>Identity:AdminRoleCode</c> is not set (no code default).</summary>
    AdminRoleNotConfigured = 3,

    /// <summary>The configured role does not exist, is passive or carries no Critical permission.</summary>
    AdminRoleInvalid = 4,

    /// <summary>The <c>IdentityBootstrapState</c> marker exists.</summary>
    AlreadyBootstrapped = 5,

    /// <summary>A company already has an unrevoked, unexpired Admin assignment.</summary>
    AdminAlreadyAssigned = 6,

    NoCompanies = 7,

    UserNameInvalid = 8,

    UserNameTaken = 9,

    DisplayNameInvalid = 10,

    PasswordMismatch = 11,

    PolicyViolation = 12,

    /// <summary>The fail-closed security event could not be written; the transaction was rolled back.</summary>
    SecurityEventRefused = 13,
}

public sealed record FounderBootstrapResult(
    FounderBootstrapStatus Status,
    Guid? FounderUserId,
    IReadOnlyList<Guid> CompanyIds,
    IReadOnlyList<string> PolicyViolations)
{
    public static FounderBootstrapResult Of(FounderBootstrapStatus status) => new(status, null, [], []);
}

/// <summary>One-shot founder bootstrap (KIM-D5, KIM-D18). Production use is a human gate (AGENTS.md).</summary>
public interface IFounderBootstrapService
{
    Task<FounderBootstrapResult> RunAsync(IFounderBootstrapConsole console, CancellationToken cancellationToken);
}

/// <summary>
/// <c>identity.founder.bootstrapped.v1</c>: written to the Identity outbox in the bootstrap transaction. Group level
/// (no company); the consumer is Platform. Carries no user name, password, hash or stamp.
/// </summary>
public sealed record IdentityFounderBootstrappedV1(Guid UserId, DateTimeOffset OccurredAt)
{
    public const string EventType = "identity.founder.bootstrapped.v1";

    public const int SchemaVersion = 1;
}
