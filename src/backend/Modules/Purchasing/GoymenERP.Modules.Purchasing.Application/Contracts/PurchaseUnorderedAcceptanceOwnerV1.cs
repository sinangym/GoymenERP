using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Create-only recorded occurrence. ExpectedVersion is 0; no price, stock movement or invoice identifier.
/// The supplier/account, subject, approved unit profile and exact document versions need real owner validation.</summary>
public sealed record RecordPurchaseUnorderedAcceptanceV1(Guid AcceptanceId, Guid AcceptanceLineId, string ExpectedVersion,
    Guid SupplierPartyId, Guid CompanyAccountId, string ExpectedPartyVersion, string ExpectedAccountVersion,
    PurchaseUnorderedAcceptanceSubjectV1 Subject, Guid UnitId, ProfileVersionV1 UnitProfile, string AcceptedQuantity,
    DateTimeOffset OccurredAtUtc, PurchaseAcceptanceDocumentEvidenceV1 Evidence,
    IReadOnlyList<PurchaseAcceptanceDocumentEvidenceV1> ControlEvidence);

public sealed record PurchaseUnorderedAcceptanceRecordV1(PurchaseUnorderedAcceptanceSourceV1 Source,
    string PartyVersion, string AccountVersion, ProfileVersionV1 UnitProfile, string RecordedBy, DateTimeOffset RecordedAtUtc);
public sealed record PurchaseUnorderedAcceptanceWriteV1(PurchaseUnorderedAcceptanceRecordV1 Record,
    bool IsReplay, string OriginalCorrelationId);

public interface IPurchaseUnorderedAcceptanceServiceV1
{
    Task<PurchaseUnorderedAcceptanceWriteV1> RecordAsync(RecordPurchaseUnorderedAcceptanceV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>Required actual owner bridge for ALS-D130: validate the actual company, active supplier/account and
/// their versions, exact goods/service identity/version, approved unit profile/scale, and clean immutable Documents
/// evidence/version/hash. The service checks the existing recording permission. Recording an actual occurrence does
/// not require prior departmental need, commercial, accounting or payment approval. Control evidence may be empty
/// or still pending; recording it never means those controls are approved. Fence consumer authority after awaited owner reads.
/// No permissive implementation is registered here: missing bridge means 503 and no new acceptance.</summary>
public interface IPurchaseUnorderedAcceptancePrerequisitesV1
{
    Task ValidateAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV1 request,
        Func<CancellationToken, Task> requireStill, CancellationToken cancellationToken);
}

public interface IPurchaseUnorderedAcceptanceStoreV1
{
    Task<PurchaseUnorderedAcceptanceWriteV1> RecordAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV1 request,
        PurchasingWriteEnvelope envelope, string requestHash,
        Func<CancellationToken, Task<PurchaseUnorderedAcceptanceRecordV1>> prepare,
        Func<CancellationToken, Task> requireStill, CancellationToken cancellationToken);
}

public static class PurchaseUnorderedAcceptanceOwnerContractV1
{
    public const string RecordCommand = "purchasing.unordered-acceptance.record.v1";
    public const int DownDataGuard = 52320;

    public static bool Valid(RecordPurchaseUnorderedAcceptanceV1? r) => r is not null && r.ExpectedVersion == "0"
        && r.AcceptanceId != Guid.Empty && r.AcceptanceLineId != Guid.Empty && r.SupplierPartyId != Guid.Empty
        && r.CompanyAccountId != Guid.Empty && Version(r.ExpectedPartyVersion) && Version(r.ExpectedAccountVersion)
        && Subject(r.Subject) && r.UnitId != Guid.Empty && r.UnitProfile is { ProfileId: var profile, Version: > 0 } && profile != Guid.Empty
        && PurchaseInvoiceAcceptanceSelectionContractV1.TryQuantity(r.AcceptedQuantity, false, out _)
        && r.OccurredAtUtc.Offset == TimeSpan.Zero && Evidence(r.Evidence) && r.ControlEvidence is not null
        && r.ControlEvidence.All(Evidence) && r.ControlEvidence.Select(x => (x.DocumentId, x.DocumentVersion, x.SourceLineId))
            .Distinct().Count() == r.ControlEvidence.Count;

    public static bool Valid(PurchaseUnorderedAcceptanceRecordV1? record)
    {
        if (record?.Source is not { } s || s.CompanyId == Guid.Empty || s.Version != 1
            || s.UninvoicedQuantity != s.AcceptedQuantity || !Version(record.RecordedBy, 128)
            || record.RecordedAtUtc.Offset != TimeSpan.Zero) return false;
        return Valid(Request(record));
    }

    public static RecordPurchaseUnorderedAcceptanceV1 Request(PurchaseUnorderedAcceptanceRecordV1 record)
    {
        var s = record.Source;
        return new(s.AcceptanceId, s.AcceptanceLineId, "0", s.SupplierPartyId, s.CompanyAccountId, record.PartyVersion,
            record.AccountVersion, s.Subject, s.UnitId, record.UnitProfile, s.AcceptedQuantity, s.OccurredAtUtc, s.Evidence, s.ControlEvidence);
    }

    public static RecordPurchaseUnorderedAcceptanceV1 Freeze(RecordPurchaseUnorderedAcceptanceV1 request) => request with
    { ControlEvidence = request.ControlEvidence is null ? null! : Array.AsReadOnly(request.ControlEvidence.ToArray()) };
    public static PurchaseUnorderedAcceptanceRecordV1 Freeze(PurchaseUnorderedAcceptanceRecordV1 record) => record with
    { Source = record.Source with { ControlEvidence = Array.AsReadOnly(record.Source.ControlEvidence.ToArray()) } };

    private static bool Subject(PurchaseUnorderedAcceptanceSubjectV1? subject) => subject switch
    {
        PurchaseUnorderedGoodsSubjectV1 goods => goods.ProductId != Guid.Empty && Version(goods.ProductVersion),
        PurchaseUnorderedServiceSubjectV1 service => service.ServiceTypeId != Guid.Empty && Version(service.ServiceTypeVersion),
        _ => false,
    };
    private static bool Version(string? value, int maximum = PurchaseAcceptanceParticipantContractV1.ProductVersionMaximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    private static bool Evidence(PurchaseAcceptanceDocumentEvidenceV1? e) => e is not null && e.DocumentId != Guid.Empty
        && e.DocumentVersion > 0 && e.Sha256 is { Length: 64 } && e.Sha256.All(char.IsAsciiHexDigit)
        && !string.IsNullOrWhiteSpace(e.SourceLineId) && e.SourceLineId == e.SourceLineId.Trim();
}
