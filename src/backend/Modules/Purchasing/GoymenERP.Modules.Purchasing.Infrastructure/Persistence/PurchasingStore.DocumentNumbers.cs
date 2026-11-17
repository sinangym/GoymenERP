using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    private const string NumberOwnerUnavailable = "purchasing_document_number_owner_unavailable";

    private async Task RequireNumberAuthorityAsync(Guid company, PurchasingWriteEnvelope envelope, CancellationToken token)
    {
        void Identity()
        {
            if (numberAuthority is null || numberUser is null) throw Refused(NumberOwnerUnavailable, "Numaralama yetki sağlayıcısı kullanılamıyor.");
            if (!numberUser.IsAuthenticated || numberUser.UserId != envelope.ActorId || !companyContext.IsResolved
                || companyContext.CompanyId != company || !numberUser.CanAccessCompany(company))
                throw Refused(PurchasingStoreCodes.CompanyContextMismatch, "Numaralama şirket ve aktör bağlamı değişti.");
        }
        Identity();
        var grant = await numberAuthority!.RevalidatePermissionScopeAsync(envelope.PermissionCode, company, token);
        Identity();
        if (grant is not { IsGranted: true, IsCompanyWide: true })
            throw Refused(PurchasingStoreCodes.CompanyContextMismatch, "Numaralama yetkisi artık geçerli değil.");
    }

    private async Task<SettingsDocumentNumberFormatV1> NumberPolicyAsync(Guid company, PurchasingWriteEnvelope envelope, CancellationToken token)
    {
        await RequireNumberAuthorityAsync(company, envelope, token);
        if (documentNumbers is null) throw Refused(NumberOwnerUnavailable, "Belge numara sağlayıcısı kullanılamıyor.");
        try
        {
            var policy = await documentNumbers.GetPolicyAsync(company, SettingsDocumentSeriesV1.PurchaseOrder, token);
            await RequireNumberAuthorityAsync(company, envelope, token);
            if (policy.CompanyId != company || policy.Series != SettingsDocumentSeriesV1.PurchaseOrder
                || !long.TryParse(policy.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1
                || policy.Version != version.ToString(CultureInfo.InvariantCulture)
                || policy.Prefix is not { Length: >= 1 and <= 16 } || policy.Prefix.Any(character => !char.IsAsciiLetterUpper(character) && !char.IsAsciiDigit(character))
                || policy.SequenceDigits is < 1 or > 18)
                throw Refused(NumberOwnerUnavailable, "Belge numara politikası geçersiz.");
            return policy;
        }
        catch (SettingsPrerequisiteException exception) { throw NumberFailure(exception); }
        catch (UnauthorizedAccessException) { throw Refused(PurchasingStoreCodes.CompanyContextMismatch, "Belge numara yetkisi geçersiz."); }
    }

    private async Task<SettingsDocumentNumberReservationV1> ReserveNumberAsync(IDbTransaction transaction, Guid company, Guid orderId,
        DateOnly date, SettingsDocumentNumberFormatV1 format, PurchasingWriteEnvelope envelope, CancellationToken token)
    {
        if (documentNumbers is null) throw Refused(NumberOwnerUnavailable, "Belge numara sağlayıcısı kullanılamıyor.");
        try
        {
            var reserved = await documentNumbers.ReservePurchaseOrderNumberAsync(transaction, company, orderId, date, format.Version,
                NumberMetadata(company, envelope), token);
            await RequireNumberAuthorityAsync(company, envelope, token);
            if (reserved.CompanyId != company || reserved.Series != SettingsDocumentSeriesV1.PurchaseOrder || reserved.DocumentId != orderId
                || reserved.BusinessDate != date || reserved.FormatVersion != format.Version || reserved.Sequence < 1
                || string.IsNullOrWhiteSpace(reserved.Number) || reserved.Number.Length > 64 || reserved.Number.Any(char.IsControl))
                throw Refused(NumberOwnerUnavailable, "Belge numara kanıtı geçersiz.");
            var expectedNumber = string.Create(CultureInfo.InvariantCulture,
                $"{format.Prefix}-{date.Year:D4}-{reserved.Sequence.ToString(CultureInfo.InvariantCulture).PadLeft(format.SequenceDigits, '0')}");
            if (!string.Equals(expectedNumber, reserved.Number, StringComparison.Ordinal))
                throw Refused(NumberOwnerUnavailable, "Belge numarası kayıtlı biçimle uyuşmuyor.");
            return reserved;
        }
        catch (SettingsPrerequisiteException exception) { throw NumberFailure(exception); }
        catch (UnauthorizedAccessException) { throw Refused(PurchasingStoreCodes.CompanyContextMismatch, "Belge numara yetkisi geçersiz."); }
    }

    private async Task CancelNumberAsync(IDbTransaction transaction, PurchaseOrderRow order, string reason,
        PurchasingWriteEnvelope envelope, CancellationToken token)
    {
        if (documentNumbers is null) throw Refused(NumberOwnerUnavailable, "Belge numara sağlayıcısı kullanılamıyor.");
        try
        {
            await RequireNumberAuthorityAsync(order.CompanyId, envelope, token);
            await documentNumbers.CancelAsync(transaction, order.CompanyId, SettingsDocumentSeriesV1.PurchaseOrder, order.Id,
                reason, NumberMetadata(order.CompanyId, envelope), token);
            await RequireNumberAuthorityAsync(order.CompanyId, envelope, token);
        }
        catch (SettingsPrerequisiteException exception) { throw NumberFailure(exception); }
        catch (UnauthorizedAccessException) { throw Refused(PurchasingStoreCodes.CompanyContextMismatch, "Belge numara yetkisi geçersiz."); }
    }

    private static SettingsCommandMetadataV1 NumberMetadata(Guid company, PurchasingWriteEnvelope envelope) =>
        new(company, envelope.ActorId, envelope.CorrelationId, envelope.CausationId, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent, envelope.IdempotencyKey);

    private static PurchasingStoreException NumberFailure(SettingsPrerequisiteException exception) => Refused(
        exception.Code is SettingsDocumentNumberSeriesProblemCodesV1.VersionConflict or SettingsDocumentNumberSeriesProblemCodesV1.IdempotencyConflict
            or SettingsDocumentNumberSeriesProblemCodesV1.DocumentAlreadyNumbered ? PurchasingStoreCodes.ConcurrentChange : NumberOwnerUnavailable,
        "Belge numarası ayrılamadı; şirketin kayıtlı numara biçimini ve güncel sürümü kontrol edin.");
}
