using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;

namespace GoymenERP.Modules.Accounting.Application.Inbox;

public sealed class CreditMovementProposalInboxConsumerV1(ICreditMovementProposalStoreV1 store) : IWorkInboxConsumerV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public Task HandleAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken) => store.AcceptAsync(envelope, cancellationToken);
    public static CreditMovementSourceV1 Read(WorkInboxEventV1 envelope, ICompanyContext company)
    {
        if (!company.IsResolved || company.CompanyId == Guid.Empty || company.CompanyId != envelope.CompanyId
            || envelope.SourceName != CreditMovementProposalInboxV1.SourceName || envelope.EventType != CreditMovementProposalInboxV1.EventType
            || envelope.Version != 1 || envelope.EventId == Guid.Empty || envelope.OccurredAtUtc == default || envelope.OccurredAtUtc.Offset != TimeSpan.Zero
            || !Text(envelope.CorrelationId, 128) || envelope.CausationId is { Length: > 128 }) throw Invalid();
        CreditMovementSourceV1? value;
        try { value = JsonSerializer.Deserialize<CreditMovementSourceV1>(envelope.PayloadJson, Json); }
        catch (JsonException) { throw Invalid(); }
        if (value is null || value.CompanyId != envelope.CompanyId || value.EventId != envelope.EventId
            || value.MovementId == Guid.Empty || value.CreditAccountId == Guid.Empty || value.CurrencyId == Guid.Empty
            || value.Kind is not ("Drawdown" or "Repayment" or "InterestAccrual" or "OpeningBalance")
            || value.SourceDocument is not { } source || !Text(source.Reference, 200) || !Text(source.Revision, 100) || source.DocumentDate == default
            || value.TransactionDate == default || value.AccountingDate == default || !Text(value.ApprovedBy, 128) || !Text(value.Reason, 1000)
            || value.Components is not { Count: > 0 and <= 6 } || value.Components.Any(x => x is null)) throw Invalid();
        if (value.Components.Select(x => x.Kind).Distinct(StringComparer.Ordinal).Count() != value.Components.Count) throw Invalid();
        foreach (var part in value.Components)
            if (part.Kind is not ("principal" or "interest" or "bsmv" or "kkdf" or "commission" or "fee") || !Amount(part.Amount)
                || part.FunctionalAmount is { } functional && !Amount(functional)) throw Invalid();
        if (!value.Components.Any(x => x.Amount.Any(c => c is >= '1' and <= '9'))
            || value.Kind is "Drawdown" or "OpeningBalance" && !value.Components.Any(x => x.Kind == "principal" && x.Amount.Any(c => c is >= '1' and <= '9'))
            || value.Kind == "InterestAccrual" && value.Components.Any(x => x.Kind == "principal")) throw Invalid();
        if (value.ReversesMovementId is { } reverse && (reverse != value.MovementId || value.Kind != "OpeningBalance")) throw Invalid();
        if (value.BankEffect is { } bank && (value.Kind is not ("Drawdown" or "Repayment") || bank.BankAccountId == Guid.Empty
            || !Amount(bank.Amount) || !bank.Amount.Any(c => c is >= '1' and <= '9') || !Text(bank.SourceReference, 200))) throw Invalid();
        return value;
    }
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value == value.Trim();
    private static bool Amount(string? value)
    {
        if (value is null || value.Length is < 1 or > 128 || value.Any(c => c is not (>= '0' and <= '9') && c != '.')) return false;
        var parts = value.Split('.');
        return parts.Length <= 2 && parts.All(x => x.Length > 0);
    }
    private static OutboxPermanentFailureException Invalid() => new("credit_movement_source_invalid", "Kredi hareketinin değişmez kaynağı doğrulanamadı.");
}
