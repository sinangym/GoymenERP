using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.SalesContracts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// S-11 (SAT-D28, NS-13, SAT-D346): one sales contract of the company (<c>sales.Contracts</c>). The contract facts never change;
/// only the discount decision moves once, PendingDiscountApproval → Active | DiscountRejected (the write guard, the trigger and
/// the checks). The payment plan and the discount rules are JSON columns of the row (they are read and written with the contract,
/// never alone); amounts and rates are invariant decimal text, never JSON numbers.
/// </summary>
internal sealed class ContractRow : ISalesTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int ContractNo { get; set; }
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public Guid MoneyProfileId { get; set; }
    public string MoneyProfileVersion { get; set; } = string.Empty;
    public int MoneyScale { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidThrough { get; set; }
    public string InstallmentsJson { get; set; } = string.Empty;
    public string DiscountRulesJson { get; set; } = string.Empty;
    public string? Clauses { get; set; }

    /// <summary>SAT-D346: the limit in force at creation; null = not configured (every discount went to approval).</summary>
    public decimal? DiscountLimitPercent { get; set; }

    /// <summary>NS-13 / SAT-D346: whether the discount needed approval when the contract was recorded (CK_Contracts_State).</summary>
    public bool DiscountApprovalRequired { get; set; }
    public int State { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>The approver or rejecter — never the creator (CK_Contracts_MakerChecker).</summary>
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>S-11 (ADR-011 §2–§8): the contract commands' own attempt records (append-only).</summary>
internal sealed class ContractAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;

    /// <summary>1 = a stored contract (<see cref="ContractId"/> set); 2 = a recorded terminal refusal (<see cref="RefusalJson"/> set).</summary>
    public int Outcome { get; set; }
    public Guid? ContractId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal static class SalesContractRows
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static ContractRow ToRow(SalesContract item, string correlationId) => new()
    {
        Id = item.Id,
        CompanyId = item.CompanyId,
        ContractNo = item.ContractNo,
        PartyId = item.PartyId,
        CompanyAccountId = item.CompanyAccountId,
        CurrencyId = item.CurrencyId,
        MoneyProfileId = item.MoneyProfileId,
        MoneyProfileVersion = item.MoneyProfileVersion,
        MoneyScale = item.MoneyScale,
        ValidFrom = item.ValidFrom,
        ValidThrough = item.ValidThrough,
        InstallmentsJson = SerializeInstallments(item.Installments),
        DiscountRulesJson = SerializeDiscountRules(item.DiscountRules),
        Clauses = item.Clauses,
        DiscountLimitPercent = item.DiscountLimitPercent,
        DiscountApprovalRequired = item.DiscountApprovalRequired,
        State = (int)item.State,
        CreatedBy = item.CreatedBy,
        CreatedAtUtc = item.CreatedAtUtc,
        DecidedBy = item.Decision?.DecidedBy,
        DecidedAtUtc = item.Decision?.DecidedAtUtc,
        RejectionReason = item.Decision?.RejectionReason,
        Version = item.Version,
        UpdatedAtUtc = item.UpdatedAtUtc,
        CorrelationId = correlationId.Trim(),
    };

    /// <summary>Rebuilds the domain contract through its own rules; a corrupt row is refused, never repaired.</summary>
    public static SalesContract Restore(ContractRow row)
    {
        if (!Enum.IsDefined((SalesContractState)row.State))
        {
            throw new InvalidOperationException("Kayıtlı sözleşme durumu tanınmıyor.");
        }

        var decision = row.DecidedBy is null && row.DecidedAtUtc is null && row.RejectionReason is null
            ? null
            : new SalesContractDecision(
                row.DecidedBy ?? throw new InvalidOperationException("Kayıtlı sözleşme kararı karar vereni taşımıyor."),
                row.DecidedAtUtc ?? throw new InvalidOperationException("Kayıtlı sözleşme kararı zamanı taşımıyor."),
                row.RejectionReason);
        var contract = SalesContract.Restore(row.Id, row.CompanyId, row.ContractNo, row.PartyId, row.CompanyAccountId, row.CurrencyId,
            row.MoneyProfileId, row.MoneyProfileVersion, row.MoneyScale, row.ValidFrom, row.ValidThrough,
            RestoreInstallments(row.InstallmentsJson, row.CurrencyId, row.MoneyScale), RestoreDiscountRules(row.DiscountRulesJson),
            row.Clauses, row.DiscountLimitPercent, (SalesContractState)row.State, row.CreatedBy, row.CreatedAtUtc, decision,
            row.Version, row.UpdatedAtUtc);
        return contract.DiscountApprovalRequired == row.DiscountApprovalRequired
            ? contract
            : throw new InvalidOperationException("Kayıtlı sözleşmenin onay gereği olgularıyla uyuşmuyor.");
    }

    /// <summary>Writes the one decision onto the tracked row: the decision columns, the version and the time only.</summary>
    public static void ApplyDecision(ContractRow row, SalesContract item)
    {
        row.State = (int)item.State;
        row.DecidedBy = item.Decision?.DecidedBy;
        row.DecidedAtUtc = item.Decision?.DecidedAtUtc;
        row.RejectionReason = item.Decision?.RejectionReason;
        row.Version = item.Version;
        row.UpdatedAtUtc = item.UpdatedAtUtc;
    }

    internal static string SerializeInstallments(IReadOnlyList<SalesContractInstallment> installments) =>
        JsonSerializer.Serialize(installments
            .Select(item => new InstallmentJson(item.InstallmentNo, item.DueDate,
                item.Amount.Amount.ToString(CultureInfo.InvariantCulture)))
            .ToArray(), Json);

    internal static string SerializeDiscountRules(IReadOnlyList<SalesContractDiscountRule> rules) =>
        JsonSerializer.Serialize(rules
            .Select(rule => new DiscountRuleJson(rule.RuleNo, rule.Kind.ToString(), rule.Label,
                rule.RatePercent.ToString(CultureInfo.InvariantCulture)))
            .ToArray(), Json);

    private static SalesContractInstallment[] RestoreInstallments(string json, Guid currencyId, int scale)
    {
        try
        {
            var items = JsonSerializer.Deserialize<InstallmentJson[]>(json, Json)
                ?? throw new InvalidOperationException("Kayıtlı ödeme planı boş.");
            return [.. items.Select(item => item is null
                ? throw new InvalidOperationException("Kayıtlı taksit boş.")
                : new SalesContractInstallment(item.InstallmentNo, item.DueDate, Money.Of(Decimal(item.Amount), currencyId, scale)))];
        }
        catch (Exception exception) when (exception is JsonException or MoneyRuleException or ArgumentException)
        {
            throw new InvalidOperationException("Kayıtlı ödeme planı okunamadı.", exception);
        }
    }

    private static SalesContractDiscountRule[] RestoreDiscountRules(string json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<DiscountRuleJson[]>(json, Json)
                ?? throw new InvalidOperationException("Kayıtlı iskonto kuralları boş.");
            return [.. items.Select(item => item is null || !Enum.TryParse<SalesContractDiscountKind>(item.Kind, ignoreCase: false, out var kind)
                    || !Enum.IsDefined(kind) || !string.Equals(kind.ToString(), item.Kind, StringComparison.Ordinal)
                ? throw new InvalidOperationException("Kayıtlı iskonto kuralı okunamadı.")
                : new SalesContractDiscountRule(item.RuleNo, kind, item.Label, Decimal(item.RatePercent)))];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Kayıtlı iskonto kuralları okunamadı.", exception);
        }
    }

    private static decimal Decimal(string text) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException("Kayıtlı ondalık metin okunamadı.");

    private sealed record InstallmentJson(int InstallmentNo, DateOnly DueDate, string Amount);

    private sealed record DiscountRuleJson(int RuleNo, string Kind, string Label, string RatePercent);
}
