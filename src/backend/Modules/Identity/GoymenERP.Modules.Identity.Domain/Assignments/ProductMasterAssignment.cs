namespace GoymenERP.Modules.Identity.Domain.Assignments;

public enum ProductMasterOperation { Read = 1, DraftCreate = 2, DraftBasicEdit = 3 }

// Explicit Identity-owned global assignment. A company RoleAssignment cannot substitute
// for this record. Its construction is data preparation, not an authenticated grant API.
public sealed class ProductMasterAssignment
{
    public const string RequiredRoleCode = "SYSTEM_ADMINISTRATOR";

    private ProductMasterAssignment() { }

    public ProductMasterAssignment(Guid id, Guid userId, Guid roleId, ProductMasterOperation operation,
        Guid issuerUserId, Guid issuerExternalIdentityId, Guid decisionId, string decisionReference,
        DateTimeOffset validFromUtc, DateTimeOffset? validUntilUtc)
    {
        if (new[] { id, userId, roleId, issuerUserId, issuerExternalIdentityId, decisionId }.Any(x => x == Guid.Empty)
            || operation is not (ProductMasterOperation.Read or ProductMasterOperation.DraftCreate or ProductMasterOperation.DraftBasicEdit))
            throw new ArgumentException("An explicit Product assignment requires exact identities and operation.");
        ValidateReference(decisionReference);
        if (validFromUtc.Offset != TimeSpan.Zero || validUntilUtc?.Offset != TimeSpan.Zero && validUntilUtc.HasValue
            || validUntilUtc.HasValue && validUntilUtc <= validFromUtc)
            throw new ArgumentException("Explicit assignment validity must be UTC and ordered.");
        Id = id;
        UserId = userId;
        RoleId = roleId;
        Operation = operation;
        IssuerUserId = issuerUserId;
        IssuerExternalIdentityId = issuerExternalIdentityId;
        DecisionId = decisionId;
        DecisionReference = decisionReference;
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public ProductMasterOperation Operation { get; private set; }
    public Guid IssuerUserId { get; private set; }
    public Guid IssuerExternalIdentityId { get; private set; }
    public Guid DecisionId { get; private set; }
    public string DecisionReference { get; private set; } = null!;
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset? ValidUntilUtc { get; private set; }
    public bool IsActive { get; private set; }
    public Guid? RevocationDecisionId { get; private set; }
    public string? RevocationDecisionReference { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Revoke(Guid decisionId, string decisionReference, DateTimeOffset occurredAtUtc)
    {
        if (!IsActive || decisionId == Guid.Empty || decisionId == DecisionId || occurredAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A fresh explicit revocation decision is required.");
        ValidateReference(decisionReference);
        IsActive = false;
        RevocationDecisionId = decisionId;
        RevocationDecisionReference = decisionReference;
        RevokedAtUtc = occurredAtUtc;
    }

    private static void ValidateReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500 || value != value.Trim() || value.Any(char.IsControl))
            throw new ArgumentException("A canonical explicit decision reference is required.", nameof(value));
    }
}
