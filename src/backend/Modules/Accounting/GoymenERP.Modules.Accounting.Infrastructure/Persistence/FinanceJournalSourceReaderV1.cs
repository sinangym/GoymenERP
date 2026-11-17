using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal static class FinanceJournalSourceReaderV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal sealed record Fact(Guid Id, Guid CompanyId, string Kind, string Version, Guid CurrencyId,
        string Amount, string ProfileId, string ProfileVersion, DateOnly OccurredOn, DateTimeOffset RecordedAtUtc);

    internal static Fact Read(WorkInboxEventV1 envelope, ICompanyContext company)
    {
        if (!company.IsResolved || company.CompanyId == Guid.Empty || company.CompanyId != envelope.CompanyId
            || envelope.SourceName != FinanceJournalSourceInboxV1.SourceName || envelope.Version != 1
            || envelope.EventId == Guid.Empty || !Utc(envelope.OccurredAtUtc)
            || string.IsNullOrWhiteSpace(envelope.PayloadJson)
            || !Text(envelope.CorrelationId, 128) || envelope.CausationId is { Length: > 128 }) throw Invalid();
        try
        {
            using var json = JsonDocument.Parse(envelope.PayloadJson);
            if (DuplicateProperties(json.RootElement)) throw Invalid();
            return envelope.EventType switch
            {
                NormalJournalSourceKindsV1.RecordedExpensePayment => Expense(
                    JsonSerializer.Deserialize<FinanceExpensePaymentRecordedV1>(envelope.PayloadJson, Json), envelope),
                NormalJournalSourceKindsV1.RecordedCashTransfer => Transfer(
                    JsonSerializer.Deserialize<FinanceCashTransferRecordedV1>(envelope.PayloadJson, Json), envelope),
                _ => throw Invalid()
            };
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static Fact Expense(FinanceExpensePaymentRecordedV1? payload, WorkInboxEventV1 envelope)
    {
        if (payload?.Source is not { } source || payload.EventId != envelope.EventId || source.CompanyId != envelope.CompanyId
            || source.Status != ExpensePaymentStatus.Recorded || !source.PaymentConfirmed
            || !Text(source.PreparedBy, 128) || !Text(source.ApprovedBy, 128) || source.PreparedBy == source.ApprovedBy
            || !Text(source.RecordedBy, 128) || !Utc(source.PreparedAtUtc) || source.ApprovedAtUtc is not { } approved || !Utc(approved)
            || source.RecordedAtUtc is not { } recorded || !Utc(recorded) || recorded != envelope.OccurredAtUtc
            || source.Preparation is not { } preparation || preparation.Money is null
            || ExpensePaymentValidator.ValidateShape(preparation).Count != 0) throw Invalid();
        return Finish(source.Id, source.CompanyId, envelope.EventType, source.Version, preparation.Money,
            preparation.Profile, preparation.OccurredOn, recorded);
    }

    private static Fact Transfer(FinanceCashTransferRecordedV1? payload, WorkInboxEventV1 envelope)
    {
        if (payload?.Source is not { } source || payload.EventId != envelope.EventId || source.CompanyId != envelope.CompanyId
            || !source.TransferConfirmed || !Text(source.RecordedBy, 128) || !Utc(source.RecordedAtUtc) || source.RecordedAtUtc != envelope.OccurredAtUtc
            || source.Preparation is not { } preparation || preparation.Kind != CashTransferKind.SameCurrency || preparation.Rate is not null
            || preparation.SourceMoney is null || preparation.TargetMoney is null || !Profile(preparation.SourceProfile) || !Profile(preparation.TargetProfile)
            || CashTransferValidator.ValidateShape(preparation).Count != 0) throw Invalid();
        return Finish(source.Id, source.CompanyId, envelope.EventType, source.Version, preparation.SourceMoney,
            preparation.SourceProfile, preparation.OccurredOn, source.RecordedAtUtc);
    }

    private static Fact Finish(Guid id, Guid company, string kind, string version, MoneyText? money,
        ProfileReference? profile, DateOnly date, DateTimeOffset recorded)
    {
        if (id == Guid.Empty || !long.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 1 || version != parsed.ToString(CultureInfo.InvariantCulture) || date == default
            || money is null || money.CurrencyId == Guid.Empty || !NormalJournalDraftSourcePreparationV1.TryExactAmount(money.Amount, out _)
            || !Profile(profile)) throw Invalid();
        return new(id, company, kind, version, money.CurrencyId, money.Amount, profile!.ProfileId, profile.Version, date, recorded);
    }
    private static bool Profile(ProfileReference? profile) => profile is not null && Text(profile.ProfileId, 128) && Text(profile.Version, 128);
    private static bool Text(string? text, int maximum) => !string.IsNullOrWhiteSpace(text) && text == text.Trim() && text.Length <= maximum;
    private static bool Utc(DateTimeOffset time) => time != default && time.Offset == TimeSpan.Zero;
    private static bool DuplicateProperties(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count()
            || value.EnumerateObject().Any(x => DuplicateProperties(x.Value)),
        JsonValueKind.Array => value.EnumerateArray().Any(DuplicateProperties),
        _ => false
    };
    private static OutboxPermanentFailureException Invalid() =>
        new("finance_journal_source_invalid", "Muhasebeye gelen Finans kaynağı doğrulanamadı.");
}
