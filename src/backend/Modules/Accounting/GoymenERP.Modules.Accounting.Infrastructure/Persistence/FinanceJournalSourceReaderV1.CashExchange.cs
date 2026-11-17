using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal static partial class FinanceJournalSourceReaderV1
{
    private static readonly JsonSerializerOptions SnapshotJson = FinanceJson.CreateOptions();

    private static Fact Exchange(FinanceCashExchangeRecordedV1? payload, WorkInboxEventV1 envelope)
    {
        if (payload is null || payload.EventId != envelope.EventId || payload.Source is not { } source
            || !ValidExchange(source, envelope.CompanyId) || source.RecordedAtUtc != envelope.OccurredAtUtc) throw Invalid();
        var value = source.Preparation;
        return Finish(source.Id, source.CompanyId, envelope.EventType, source.Version, value.SourceMoney,
            value.SourceProfile, value.OccurredOn, source.RecordedAtUtc) with
        {
            CashExchange = ExchangeView(source, null)
        };
    }

    private static Fact BookValue(FinanceCashExchangeBookValueApprovedV1? payload, WorkInboxEventV1 envelope)
    {
        if (payload is null || payload.EventId != envelope.EventId || payload.Source is not { } source
            || payload.BookValue is not { } value || !ValidApprovedExchange(source, value, envelope.CompanyId)
            || value.DecidedAtUtc != envelope.OccurredAtUtc) throw Invalid();
        // OwnerProfile is the actual source-leg proof, not a fabricated TL rounding profile.
        // Journal preparation additionally requires the actual TRY target and the approved Accounting binding.
        return Finish(value.Id, source.CompanyId, envelope.EventType, value.Version,
            new MoneyText(value.SourceBookValueTl, SettingsCurrencyIdentitiesV1.TurkishLira),
            source.Preparation.SourceProfile, source.Preparation.OccurredOn, value.DecidedAtUtc!.Value) with
        {
            CashExchange = ExchangeView(source, value)
        };
    }

    internal static bool ValidApprovedExchange(CashTransferView source, CashExchangeBookValueViewV1 value, Guid companyId) =>
        ValidExchange(source, companyId) && value.Id != Guid.Empty && value.CompanyId == companyId
        && value.TransferId == source.Id && value.TransferVersion == source.Version && value.Version == "2"
        && value.Status == CashExchangeBookValueStatusV1.Approved && value.TransferSourceHash == TransferHash(source)
        && NormalJournalDraftSourcePreparationV1.TryExactAmount(value.SourceBookValueTl, out _)
        && value.Evidence is { DocumentId: var document, VersionNo: > 0, Sha256: { Length: 64 } hash }
        && document != Guid.Empty && hash.All(Uri.IsHexDigit) && Text(value.Reason, 2000)
        && Guid.TryParse(value.PreparedBy, out var maker) && maker != Guid.Empty
        && Guid.TryParse(value.DecidedBy, out var checker) && checker != Guid.Empty && maker != checker
        && Utc(value.PreparedAtUtc) && value.PreparedAtUtc >= source.RecordedAtUtc
        && value.DecidedAtUtc is { } decided && Utc(decided) && decided >= value.PreparedAtUtc;

    internal static string TransferHash(CashTransferView source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source, SnapshotJson)))).ToLowerInvariant();

    internal static FinanceCashExchangeValueV1? ReadStoredExchange(FinanceJournalSourceRow row)
    {
        if (row.SourceKind is not (NormalJournalSourceKindsV1.RecordedCashExchange or NormalJournalSourceKindsV1.ApprovedCashExchangeBookValue))
            return null;
        try
        {
            if (row.SourceHash != AccountingInventoryCostSourceValidationV1.Sha256(row.SourceJson)) throw Invalid();
            if (row.SourceKind == NormalJournalSourceKindsV1.RecordedCashExchange)
            {
                var source = JsonSerializer.Deserialize<FinanceCashExchangeRecordedV1>(row.SourceJson, Json);
                if (source?.Source is not { } transfer || source.EventId != row.Id || transfer.Id != row.SourceId
                    || transfer.Version != row.SourceVersion || !ValidExchange(transfer, row.CompanyId)) throw Invalid();
                return ExchangeView(transfer, null);
            }
            var approved = JsonSerializer.Deserialize<FinanceCashExchangeBookValueApprovedV1>(row.SourceJson, Json);
            if (approved is not { Source: { }, BookValue: { } } || approved.EventId != row.Id || approved.BookValue.Id != row.SourceId
                || approved.BookValue.Version != row.SourceVersion || !ValidApprovedExchange(approved.Source, approved.BookValue, row.CompanyId)) throw Invalid();
            return ExchangeView(approved.Source, approved.BookValue);
        }
        catch (Exception error) when (error is JsonException or OutboxPermanentFailureException)
        { throw new AccountingStoreException("accounting_finance_source_unreadable", "Kaydedilmiş Finans kaynak kanıtı okunamıyor.", error); }
    }

    private static bool ValidExchange(CashTransferView source, Guid companyId)
    {
        if (source.Id == Guid.Empty || source.CompanyId != companyId || !source.TransferConfirmed
            || !Text(source.RecordedBy, 128) || !Utc(source.RecordedAtUtc) || source.Version != "1"
            || source.Preparation is not { Kind: CashTransferKind.CurrencyExchange } preparation
            || CashTransferValidator.ValidateShape(preparation).Count != 0 || source.Exchange is not { } exchange
            || !Profile(preparation.SourceProfile) || !Profile(preparation.TargetProfile)) return false;
        try
        {
            var expected = CashTransferExchangeCalculatorV1.Calculate(companyId, preparation.OccurredOn,
                preparation.SourceMoney, preparation.TargetMoney, preparation.SourceProfile, preparation.TargetProfile,
                exchange.SourceComparison, exchange.TargetComparison);
            // Null optional references are historical omissions; no value is backfilled into a source snapshot.
            expected = expected with { SourceReferenceTl = exchange.SourceReferenceTl is null ? null : expected.SourceReferenceTl,
                TargetReferenceTl = exchange.TargetReferenceTl is null ? null : expected.TargetReferenceTl };
            return JsonSerializer.Serialize(exchange, SnapshotJson) == JsonSerializer.Serialize(expected, SnapshotJson)
                && CashTransferExchangeCalculatorV1.HasValidTlReferences(exchange);
        }
        catch (Exception error) when (error is ArgumentException or SettingsPrerequisiteException) { return false; }
    }

    internal static FinanceCashExchangeValueV1 ExchangeView(CashTransferView source, CashExchangeBookValueViewV1? value) =>
        new(source.Id, value?.Id, source.Preparation.SourceMoney.CurrencyId, source.Preparation.SourceMoney.Amount,
            source.Preparation.TargetMoney.CurrencyId, source.Preparation.TargetMoney.Amount, value?.SourceBookValueTl,
            value is null ? "SourceBookValuePending"
                : source.Preparation.TargetMoney.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira
                    ? "TargetBookValueUnavailable" : "ApprovedSourceBookValue");
}
