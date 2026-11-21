namespace GoymenERP.Modules.Identity.Domain.CompanySetup;

/// <summary>
/// Explicit human-approved group eligibility. No seeder, HTTP grant command or role-name inference writes these rows.
/// This is a global Identity authorization record, not a company role assignment or a wildcard CompanyId.
/// </summary>
public sealed class CompanySetupGroupAssignment
{
    private CompanySetupGroupAssignment() { }
    public CompanySetupGroupAssignment(Guid id, Guid userId, int operation, Guid decisionId,
        string decisionReference, DateTimeOffset validFromUtc, DateTimeOffset? validUntilUtc)
    {
        if (id == Guid.Empty || userId == Guid.Empty || decisionId == Guid.Empty || operation is not (1 or 2))
            throw new ArgumentException("Explicit company-setup identities and operation are required.");
        if (validFromUtc.Offset != TimeSpan.Zero || validUntilUtc.HasValue
            && (validUntilUtc.Value.Offset != TimeSpan.Zero || validUntilUtc <= validFromUtc))
            throw new ArgumentException("Authority validity must be UTC and ordered.");
        RequireReference(decisionReference);
        Id = id; UserId = userId; Operation = operation; DecisionId = decisionId;
        DecisionReference = decisionReference; ValidFromUtc = validFromUtc; ValidUntilUtc = validUntilUtc;
    }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public int Operation { get; private set; }
    public Guid DecisionId { get; private set; }
    public string DecisionReference { get; private set; } = string.Empty;
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset? ValidUntilUtc { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid? RevocationDecisionId { get; private set; }
    public string? RevocationDecisionReference { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public void Revoke(Guid decisionId, string decisionReference, DateTimeOffset atUtc)
    {
        if (!IsActive || decisionId == Guid.Empty || decisionId == DecisionId || atUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A fresh explicit revocation decision is required.");
        RequireReference(decisionReference);
        IsActive = false; RevocationDecisionId = decisionId; RevocationDecisionReference = decisionReference; RevokedAtUtc = atUtc;
    }
    private static void RequireReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > 500 || value.Any(char.IsControl))
            throw new ArgumentException("A canonical human decision reference is required.");
    }
}
