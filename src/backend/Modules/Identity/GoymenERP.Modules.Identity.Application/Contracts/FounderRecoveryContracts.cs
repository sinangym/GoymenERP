namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>
/// The operator terminal of <c>GoymenERP.Api identity recover-founder</c> (spec ID-2r, KIM-D22/D23). A password is only
/// ever read through <see cref="ReadNewPassword"/> (no echo); it is never taken from an argument, an environment
/// variable or a file. There is no non-interactive confirmation (<c>--yes</c>/<c>--force</c> do not exist).
/// </summary>
public interface IFounderRecoveryConsole
{
    /// <summary><see langword="false"/> when stdin is redirected (<c>cmd &lt; file</c> or a pipe): the command refuses.</summary>
    bool IsInteractive { get; }

    /// <summary>Runtime operator declaration; no OS identity is inferred from ERP roles or emitted to application logs.</summary>
    FounderRecoveryOperatorDeclaration? ReadOperatorDeclaration();

    /// <summary>Reads the new password without echoing it.</summary>
    string? ReadNewPassword();

    /// <summary>Reads the repeated new password without echoing it.</summary>
    string? ReadNewPasswordConfirmation();

    /// <summary>
    /// Shows the state read in step 1 (target environment, database server and catalog without any password, the
    /// founder's user name and the companies) and asks for an explicit interactive confirmation.
    /// </summary>
    bool Confirm(FounderRecoveryConfirmation confirmation);
}

/// <summary>The two mutually exclusive sub-commands; the command never infers one from the account state.</summary>
public enum FounderRecoverySubcommand
{
    /// <summary>KIM-D22: reactivates the passive founder.</summary>
    Reactivate = 1,

    /// <summary>KIM-D23/D43: issues a one-time temporary password to the active founder.</summary>
    ResetPassword = 2,
}

/// <summary>What the operator confirms. Carries no password, hash, stamp or connection secret.</summary>
public sealed record FounderRecoveryConfirmation(
    FounderRecoverySubcommand Subcommand,
    string FounderUserName,
    string? DatabaseServer,
    string? DatabaseCatalog,
    IReadOnlyList<FounderRecoveryCompany> Companies,
    FounderRecoveryOperatorDeclaration Operator);

public sealed record FounderRecoveryCompany(Guid CompanyId, string Code, string LegalName);
public sealed record FounderRecoveryOperatorDeclaration(string OperatingSystemUser, string MachineName);

public enum FounderRecoveryStatus
{
    Completed = 1,

    /// <summary>No sub-command (or an unknown one) was given.</summary>
    SubcommandRequired = 2,

    /// <summary>The <c>IdentityBootstrapState</c> marker does not exist.</summary>
    NotBootstrapped = 3,

    /// <summary>The marker's founder has no user or local credential row (fail-closed).</summary>
    FounderNotFound = 4,

    /// <summary><c>reactivate</c> on an active founder.</summary>
    FounderActive = 5,

    /// <summary><c>reset-password</c> on a passive founder.</summary>
    FounderInactive = 6,

    /// <summary>
    /// <c>reactivate</c>: every company has an effective Admin (KIM-D17). <c>reset-password</c>: another active user can
    /// reset the founder from the screen (KIM-D16).
    /// </summary>
    NotNeeded = 7,

    /// <summary>The account changed after the operator confirmed it, or a company lock was busy.</summary>
    ConcurrentChange = 8,

    /// <summary><c>Identity:FounderRecovery:TemporaryPasswordLifetime</c> is invalid (zero, negative or unreadable).</summary>
    ConfigurationMissing = 9,

    /// <summary><c>Identity:AdminRoleCode</c> is not set, so the KIM-D17 effective Admin cannot be evaluated.</summary>
    AdminRoleNotConfigured = 10,

    /// <summary>stdin is not a terminal.</summary>
    NotInteractive = 11,

    PasswordMismatch = 12,

    PolicyViolation = 13,

    /// <summary>The operator did not confirm.</summary>
    NotConfirmed = 14,

    /// <summary>The fail-closed success security event could not be written; the transaction was rolled back.</summary>
    SecurityEventRefused = 15,
    AuditRefused = 16,
    OperatorDeclarationMissing = 17,
}

/// <summary>Stable English refusal codes (spec ID-2r proposals); the terminal text is Turkish and separate.</summary>
public static class FounderRecoveryRefusalCodes
{
    public const string SubcommandRequired = "identity.founder_recovery.subcommand_required";
    public const string NotBootstrapped = "identity.founder_recovery.not_bootstrapped";
    public const string FounderNotFound = "identity.founder_recovery.founder_not_found";
    public const string FounderActive = "identity.founder_recovery.founder_active";
    public const string FounderInactive = "identity.founder_recovery.founder_inactive";
    public const string NotNeeded = "identity.founder_recovery.not_needed";
    public const string ConcurrentChange = "identity.founder_recovery.concurrent_change";
    public const string ConfigurationMissing = "identity.founder_recovery.configuration_missing";
    public const string AdminRoleNotConfigured = "identity.founder_recovery.admin_role_not_configured";
    public const string NotInteractive = "identity.founder_recovery.not_interactive";
    public const string PasswordMismatch = "identity.founder_recovery.password_mismatch";
    public const string PolicyViolation = "identity.founder_recovery.policy_violation";
    public const string NotConfirmed = "identity.founder_recovery.not_confirmed";
    public const string SecurityEventRefused = "identity.founder_recovery.security_event_refused";
    public const string AuditRefused = "identity.founder_recovery.audit_refused";
    public const string OperatorDeclarationMissing = "identity.founder_recovery.operator_declaration_missing";

    /// <summary>The code of a refusal; <see langword="null"/> for <see cref="FounderRecoveryStatus.Completed"/>.</summary>
    public static string? Of(FounderRecoveryStatus status) => status switch
    {
        FounderRecoveryStatus.Completed => null,
        FounderRecoveryStatus.AuditRefused => AuditRefused,
        FounderRecoveryStatus.OperatorDeclarationMissing => OperatorDeclarationMissing,
        FounderRecoveryStatus.SubcommandRequired => SubcommandRequired,
        FounderRecoveryStatus.NotBootstrapped => NotBootstrapped,
        FounderRecoveryStatus.FounderNotFound => FounderNotFound,
        FounderRecoveryStatus.FounderActive => FounderActive,
        FounderRecoveryStatus.FounderInactive => FounderInactive,
        FounderRecoveryStatus.NotNeeded => NotNeeded,
        FounderRecoveryStatus.ConcurrentChange => ConcurrentChange,
        FounderRecoveryStatus.ConfigurationMissing => ConfigurationMissing,
        FounderRecoveryStatus.AdminRoleNotConfigured => AdminRoleNotConfigured,
        FounderRecoveryStatus.NotInteractive => NotInteractive,
        FounderRecoveryStatus.PasswordMismatch => PasswordMismatch,
        FounderRecoveryStatus.PolicyViolation => PolicyViolation,
        FounderRecoveryStatus.NotConfirmed => NotConfirmed,
        FounderRecoveryStatus.SecurityEventRefused => SecurityEventRefused,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}

/// <summary>
/// The outcome of one run. <see cref="CompanyIds"/> are the companies that met the precondition (reactivate: the ones
/// without an effective Admin; reset-password: the founder's membership companies). No password, hash or stamp.
/// </summary>
public sealed record FounderRecoveryResult(
    FounderRecoveryStatus Status,
    Guid? FounderUserId,
    IReadOnlyList<Guid> CompanyIds,
    IReadOnlyList<string> PolicyViolations,
    DateTimeOffset? TemporaryPasswordExpiresAtUtc)
{
    public string? RefusalCode => FounderRecoveryRefusalCodes.Of(Status);

    public static FounderRecoveryResult Of(FounderRecoveryStatus status) => new(status, null, [], [], null);
}

/// <summary>
/// ID-2r founder recovery (KIM-D22 reactivate, KIM-D23/D43 reset-password). Only the bootstrap marker's founder is ever
/// targeted. Production use is a human gate (AGENTS.md); the confirmation step is evidence for it, not a substitute.
/// </summary>
public interface IFounderRecoveryService
{
    /// <summary>
    /// Runs one sub-command; <see langword="null"/> means none (or an unknown one) was given and the run is refused
    /// with <see cref="FounderRecoveryStatus.SubcommandRequired"/> (the refusal is still recorded).
    /// </summary>
    Task<FounderRecoveryResult> RunAsync(
        FounderRecoverySubcommand? subcommand,
        IFounderRecoveryConsole console,
        CancellationToken cancellationToken);
}
