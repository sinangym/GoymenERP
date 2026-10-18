using GoymenERP.Modules.Purchasing.Domain;

namespace GoymenERP.Modules.Purchasing.Domain.Subcontracts;

/// <summary>The owner identity namespace of material involved in subcontract execution.</summary>
public enum SubcontractMaterialOwnerKindV1
{
    Company = 1,
    Counterparty = 2,
}

/// <summary>
/// Immutable reference to the physical context measured by Organization and Inventory owners.
/// Purchasing does not move or duplicate the material: owner stays explicit, the external location's
/// exact operator is the custodian, and the source physical request remains separately versioned.
/// This type deliberately carries no service quantity, stock quantity, label mutation or stock effect.
/// </summary>
public sealed class SubcontractPhysicalContextV1
{
    private SubcontractPhysicalContextV1(
        Guid companyId,
        SubcontractMaterialOwnerKindV1 materialOwnerKind,
        Guid materialOwnerId,
        Guid externalLocationId,
        string externalLocationVersion,
        Guid operatorPartyId,
        string operatorPartyVersion,
        Guid sourcePhysicalRequestId,
        string sourcePhysicalRequestVersion)
    {
        CompanyId = Identity.Require(companyId, nameof(companyId));
        if (!Enum.IsDefined(materialOwnerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(materialOwnerKind));
        }

        MaterialOwnerKind = materialOwnerKind;
        MaterialOwnerId = Identity.Require(materialOwnerId, nameof(materialOwnerId));
        ExternalLocationId = Identity.Require(externalLocationId, nameof(externalLocationId));
        ExternalLocationVersion = RequireToken(externalLocationVersion, nameof(externalLocationVersion));
        OperatorPartyId = Identity.Require(operatorPartyId, nameof(operatorPartyId));
        OperatorPartyVersion = RequireToken(operatorPartyVersion, nameof(operatorPartyVersion));
        SourcePhysicalRequestId = Identity.Require(sourcePhysicalRequestId, nameof(sourcePhysicalRequestId));
        SourcePhysicalRequestVersion = RequireToken(sourcePhysicalRequestVersion, nameof(sourcePhysicalRequestVersion));
    }

    public Guid CompanyId { get; }
    public SubcontractMaterialOwnerKindV1 MaterialOwnerKind { get; }
    public Guid MaterialOwnerId { get; }
    public Guid ExternalLocationId { get; }
    public string ExternalLocationVersion { get; }
    public Guid OperatorPartyId { get; }
    public string OperatorPartyVersion { get; }

    /// <summary>The external location operator is the exact destination custodian.</summary>
    public Guid CustodianPartyId => OperatorPartyId;

    public Guid SourcePhysicalRequestId { get; }
    public string SourcePhysicalRequestVersion { get; }

    public static SubcontractPhysicalContextV1 Create(
        Guid companyId,
        SubcontractMaterialOwnerKindV1 materialOwnerKind,
        Guid materialOwnerId,
        Guid externalLocationId,
        string externalLocationVersion,
        Guid operatorPartyId,
        string operatorPartyVersion,
        Guid sourcePhysicalRequestId,
        string sourcePhysicalRequestVersion) =>
        new(
            companyId,
            materialOwnerKind,
            materialOwnerId,
            externalLocationId,
            externalLocationVersion,
            operatorPartyId,
            operatorPartyVersion,
            sourcePhysicalRequestId,
            sourcePhysicalRequestVersion);

    private static string RequireToken(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("A canonical owner version is required.", parameterName);
        }

        return value;
    }
}
