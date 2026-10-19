using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain.Subcontracts;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public static class PurchasingSubcontractApiV1
{
    public const string Path = "/api/v1/purchasing/subcontracts";
    public const string DetailPath = Path + "/{id}";
    public const string AdmissionPath = DetailPath + "/admission";
}

public static class PurchasingSubcontractCommandsV1
{
    public const string Create = "purchasing.subcontract.create.v1";
    public const string Admit = "purchasing.subcontract.admit.v1";
}

public static class PurchasingSubcontractProblemCodesV1
{
    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";
    public const string VersionConflict = "version_conflict";
    public const string NotDraft = "purchasing_subcontract_not_draft";
    public const string ConcurrentChange = "concurrent_change";
    public const string OwnerRefused = "purchasing_subcontract_owner_refused";
}

public sealed record CreatePurchasingSubcontractLineV1(Guid LineId, Guid ServiceId,
    string ExpectedServiceVersion, Guid ServiceUnitId, string ServiceQuantity,
    Guid SourcePhysicalRequestId, string ExpectedSourcePhysicalRequestVersion);
public sealed record CreatePurchasingSubcontractDraftV1(Guid SupplierPartyId, string ExpectedSupplierPartyVersion,
    Guid CompanyAccountId, string ExpectedCompanyAccountVersion, IReadOnlyList<CreatePurchasingSubcontractLineV1> Lines);
public sealed record AdmitPurchasingSubcontractV1(string ExpectedVersion);

public sealed record PurchasingSubcontractLineViewV1(int LineNo, Guid LineId, Guid ServiceId,
    string ServiceVersion, Guid ServiceUnitId, string ServiceQuantity,
    Guid SourcePhysicalRequestId, string SourcePhysicalRequestVersion);
public sealed record PurchasingSubcontractViewV1(Guid Id, Guid CompanyId, string Status, string Version,
    Guid SupplierPartyId, string SupplierPartyVersion, Guid CompanyAccountId, string CompanyAccountVersion,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, IReadOnlyList<PurchasingSubcontractLineViewV1> Lines,
    IReadOnlyList<PurchasingSubcontractAdmissionViewV1> Admissions);
public sealed record PurchasingSubcontractAdmissionViewV1(Guid LineId, string ServiceVersion,
    Guid MaterialOwnerId, Guid ExternalLocationId, string ExternalLocationVersion,
    Guid OperatorPartyId, string OperatorPartyVersion, DateTimeOffset AdmittedAtUtc);
public sealed record PurchasingSubcontractResponseV1(PurchasingSubcontractViewV1 Order, bool IsReplay, string OriginalCorrelationId);

public interface IPurchasingSubcontractRuntimeServiceV1
{
    Task<PurchasingSubcontractResponseV1> CreateDraftAsync(CreatePurchasingSubcontractDraftV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<PurchasingSubcontractViewV1> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PurchasingSubcontractResponseV1> AdmitAsync(Guid id, AdmitPurchasingSubcontractV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

public static class PurchasingSubcontractJsonV1
{
    public static CreatePurchasingSubcontractDraftV1 ReadCreate(string json) =>
        JsonSerializer.Deserialize<CreatePurchasingSubcontractDraftV1>(json, PurchasingJson.Options)
        ?? throw new JsonException("Body is required.");
    public static AdmitPurchasingSubcontractV1 ReadAdmit(string json) =>
        JsonSerializer.Deserialize<AdmitPurchasingSubcontractV1>(json, PurchasingJson.Options)
        ?? throw new JsonException("Body is required.");
}

public sealed record PurchasingSubcontractAdmissionSnapshotStoreV1(Guid LineId,
    PurchasingCommercialLineSubjectSnapshotV1 Service, string ExpectedOrderVersion,
    Guid MaterialOwnerId, int MaterialOwnerKind, Guid ExternalLocationId, string ExternalLocationVersion,
    Guid OperatorPartyId, string OperatorPartyVersion, Guid SourcePhysicalRequestId,
    string SourcePhysicalRequestVersion, DateTimeOffset AdmittedAtUtc);

public sealed record StoredPurchasingSubcontractV1(SubcontractOrderV1 Order,
    IReadOnlyList<PurchasingSubcontractAdmissionSnapshotStoreV1> Admissions);
