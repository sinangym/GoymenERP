using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.SalesContracts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// S-11 — satış sözleşmesi kaydı (SAT-D28 EK3; NS-13, SAT-D346). The contract is a separate Sales record: number, version, state,
// validity window, installment payment plan, line/total discount rules and clauses. A discount above the configured limit needs an
// authorized approval; the limit has no default and while it is not configured every discount goes to approval. The PDF vault link
// and the agreement price tier (SAT-D58) are out of this slice.

/// <summary>The versioned command names the contract attempt record is keyed by (ADR-011 §2).</summary>
public static class SalesContractCommands
{
    /// <summary>S-11: POST /api/v1/sales/contracts.</summary>
    public const string CreateV1 = "sales.contract.create.v1";

    /// <summary>S-11 (NS-13): POST /api/v1/sales/contracts/{id}/discount-approval.</summary>
    public const string ApproveDiscountV1 = "sales.contract.discount-approve.v1";

    /// <summary>S-11 (NS-13): POST /api/v1/sales/contracts/{id}/discount-rejection (a reason).</summary>
    public const string RejectDiscountV1 = "sales.contract.discount-reject.v1";

    public static bool IsContractCommand(string? name) => name is CreateV1 or ApproveDiscountV1 or RejectDiscountV1;
}

/// <summary>Routes and technical bounds of S-11 (not policies).</summary>
public static class SalesContractApiV1
{
    public const string ContractsPath = "/api/v1/sales/contracts";
    public const string ContractDetailPath = "/api/v1/sales/contracts/{id}";
    public const string DiscountApprovalPath = "/api/v1/sales/contracts/{id}/discount-approval";
    public const string DiscountRejectionPath = "/api/v1/sales/contracts/{id}/discount-rejection";

    public const int CreatedStatus = 201;

    public const int CommandStatus = 200;

    /// <summary>Technical bound (not a policy): the most contracts one read lists (newest first).</summary>
    public const int MaximumListed = 200;

    /// <summary>The discount kinds on the wire (<see cref="SalesContractDiscountKind"/>).</summary>
    public const string LineKind = nameof(SalesContractDiscountKind.Line);

    public const string TotalKind = nameof(SalesContractDiscountKind.Total);
}

/// <summary>The stable problem codes of S-11 besides the shared draft codes.</summary>
public static class SalesContractProblemCodesV1
{
    /// <summary>422: the validity window is not ordered.</summary>
    public const string ValidityInvalid = SalesRuleCodes.ContractValidityInvalid;

    /// <summary>422: the payment plan is empty, too long, carries a non-positive or over-scale amount, or is not in due-date order.</summary>
    public const string InstallmentsInvalid = SalesRuleCodes.ContractInstallmentsInvalid;

    /// <summary>422: a discount rule is invalid (label, rate, kind or count).</summary>
    public const string DiscountInvalid = SalesRuleCodes.ContractDiscountInvalid;

    /// <summary>422: the clauses exceed their bound (never cut).</summary>
    public const string ClausesTooLong = SalesRuleCodes.ContractClausesTooLong;

    /// <summary>422: the rejection reason exceeds its bound (never cut).</summary>
    public const string ReasonTooLong = SalesRuleCodes.ContractReasonTooLong;

    /// <summary>409 (recorded under the key): the contract's creator decides its own discount (maker≠checker).</summary>
    public const string SelfDecision = SalesRuleCodes.ContractSelfDecision;

    /// <summary>422 (not recorded): the money profile of the currency is not active or not in effect today.</summary>
    public const string MoneyProfileNotInEffect = SalesRuleCodes.MoneyProfileNotInEffect;
}

/// <summary>One installment as the command gives it: due date and the exact amount as decimal text (never a JSON number).</summary>
public sealed record SalesContractInstallmentRequest(DateOnly DueDate, string Amount);

/// <summary>One discount rule as the command gives it: <c>Line</c> or <c>Total</c>, a label and the rate as decimal text.</summary>
public sealed record SalesContractDiscountRuleRequest(string Kind, string Label, string RatePercent);

/// <summary>
/// S-11 request body. The company is the server's, never an input; the party and the company account are Counterparty owner
/// references read under the command's fence; the currency's money profile is read from Settings for today's business date.
/// Both validity dates are required (an open-ended contract is not accepted).
/// </summary>
public sealed record CreateSalesContractRequest(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    DateOnly ValidFrom,
    DateOnly ValidThrough,
    IReadOnlyList<SalesContractInstallmentRequest> Installments,
    IReadOnlyList<SalesContractDiscountRuleRequest>? DiscountRules = null,
    string? Clauses = null);

public sealed record SalesContractInstallmentView(int InstallmentNo, DateOnly DueDate, string Amount);

public sealed record SalesContractDiscountRuleView(int RuleNo, string Kind, string Label, string RatePercent);

/// <summary>
/// One contract as the screens see it. <see cref="State"/> is the stored axis (<c>PendingDiscountApproval</c>, <c>Active</c>,
/// <c>DiscountRejected</c>). <see cref="DiscountLimitDefined"/> false means no limit was configured when the contract was recorded
/// (SAT-D346: every discount went to approval; the screen says "sınır tanımlı değil").
/// </summary>
public sealed record SalesContractView(
    Guid Id,
    int ContractNo,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    int MoneyScale,
    string State,
    string Version,
    DateOnly ValidFrom,
    DateOnly ValidThrough,
    IReadOnlyList<SalesContractInstallmentView> Installments,
    string PlannedTotalAmount,
    IReadOnlyList<SalesContractDiscountRuleView> DiscountRules,
    string? Clauses,
    bool DiscountApprovalRequired,
    bool DiscountLimitDefined,
    string? DiscountLimitPercent,
    bool IsInEffectToday,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? RejectionReason);

public sealed record SalesContractListResponse(IReadOnlyList<SalesContractView> Items);

/// <summary>The command's answer to the route: the contract, whether it was replayed, and the first correlation (replay header).</summary>
public sealed record SalesContractCommandResult(SalesContractView Contract, bool IsReplay, string OriginalCorrelationId);

/// <summary>The strict reader of the request bodies: unknown members, a null body, an empty id or malformed decimal text is a 400.</summary>
public static class SalesContractJson
{
    public static CreateSalesContractRequest ReadCreate(string json)
    {
        var request = JsonSerializer.Deserialize<CreateSalesContractRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        Validate(request);
        return request;
    }

    /// <summary>Shape only (the domain owns the rules): ids, list presence and bounds, decimal text and the discount kind.</summary>
    public static void Validate(CreateSalesContractRequest request)
    {
        if (request is null || request.PartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty || request.CurrencyId == Guid.Empty
            || request.Installments is null || request.Installments.Count > SalesContract.MaximumInstallments
            || request.DiscountRules?.Count > SalesContract.MaximumDiscountRules)
        {
            throw new JsonException("Invalid contract fields.");
        }

        foreach (var installment in request.Installments)
        {
            if (installment is null || installment.Amount is null)
            {
                throw new JsonException("Invalid installment.");
            }

            _ = ParseDecimal(installment.Amount, positive: true);
        }

        foreach (var rule in request.DiscountRules ?? [])
        {
            if (rule is null || rule.Label is null || rule.RatePercent is null || !TryKind(rule.Kind, out _))
            {
                throw new JsonException("Invalid discount rule.");
            }

            _ = ParseDecimal(rule.RatePercent, positive: true);
        }
    }

    /// <summary>The approval carries the expected version only (a reason is an unknown member).</summary>
    public static SalesDraftStateCommandRequest ReadApproval(string json) => SalesDraftJson.ReadStateCommand(json, allowReason: false);

    /// <summary>The rejection takes the expected version and a reason.</summary>
    public static SalesDraftStateCommandRequest ReadRejection(string json) => SalesDraftJson.ReadStateCommand(json, allowReason: true);

    /// <summary>Decimal text in the invariant shape (<c>123</c> or <c>123.45</c>); a value outside System.Decimal is a 400.</summary>
    public static decimal ParseDecimal(string text, bool positive)
    {
        SalesDraftJson.RequireDecimalShape(text, positive);
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new JsonException("Invalid decimal text.");
    }

    public static bool TryKind(string? text, out SalesContractDiscountKind kind)
    {
        switch (text)
        {
            case SalesContractApiV1.LineKind:
                kind = SalesContractDiscountKind.Line;
                return true;
            case SalesContractApiV1.TotalKind:
                kind = SalesContractDiscountKind.Total;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}

/// <summary>The canonical contents of the S-11 commands for the replay identity (ADR-011 §3).</summary>
public static class SalesContractHash
{
    public static string Compute(CreateSalesContractRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = SalesContractCommands.CreateV1,
            request.PartyId,
            request.CompanyAccountId,
            request.CurrencyId,
            request.ValidFrom,
            request.ValidThrough,
            installments = request.Installments.Select(item => new { item.DueDate, item.Amount }).ToArray(),
            discountRules = (request.DiscountRules ?? []).Select(rule => new { rule.Kind, rule.Label, rule.RatePercent }).ToArray(),
            request.Clauses,
        }, SalesDraftJson.Options));
    }

    public static string Compute(string commandName, Guid contractId, SalesDraftStateCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = commandName,
            contractId,
            request.ExpectedVersion,
            request.Reason,
        }, SalesDraftJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

/// <summary>What the store did for an S-11 command: the contract, recorded now or replayed, and the first correlation.</summary>
public sealed record SalesContractStoreResult(SalesContract Contract, SalesDraftWriteOutcome Outcome, string OriginalCorrelationId);

/// <summary>What an S-11 command key already holds: the stored contract or the recorded terminal refusal, never both.</summary>
public sealed record SalesContractReplay(SalesContract? Contract, SalesDraftTerminalRefusal? Refusal, string OriginalCorrelationId);

/// <summary>
/// The contract half of the Sales store (ADR-011 §2–§8, in its own attempt table): one save writes the contract, the attempt and
/// the audit event. The per-company number index refuses a racing number as the save's concurrent change. Another company's
/// contracts are invisible. Sales writes only its own tables.
/// </summary>
public interface ISalesContractStore
{
    /// <summary>The stored answer or recorded refusal of the key, null when the key is new; another content or actor is a 409.</summary>
    Task<SalesContractReplay?> FindContractReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a new contract of the company in one save (the row, the attempt, the audit); <paramref name="build"/> gets the next
    /// per-company number and the commit instant. A racing same key answers the winner.
    /// </summary>
    Task<SalesContractStoreResult> StoreContractAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Func<int, DateTimeOffset, SalesContract> build, SalesDraftStateAudit audit, CancellationToken cancellationToken = default);

    /// <summary>ADR-011 §7: records a terminal refusal of an S-11 decision under its key.</summary>
    Task RecordContractRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid contractId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default);

    /// <summary>
    /// One discount decision on the company's stored contract in one save (the row, the attempt, the audit). <paramref name="mutate"/>
    /// is the domain's decision; a refusal writes nothing. Null when the contract does not exist here.
    /// </summary>
    Task<SalesContractStoreResult?> DecideContractAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid contractId, Action<SalesContract, DateTimeOffset> mutate, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default);

    Task<SalesContract?> FindContractAsync(Guid contractId, CancellationToken cancellationToken = default);

    /// <summary>The company's contracts, newest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<SalesContract>> ListContractsAsync(int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// S-11 (SAT-D28, NS-13, SAT-D346): record a sales contract, approve or reject its discount, and read contracts. Company, actor and
/// authority are the server's and measured afresh before anything is read, after every awaited read and right before the single
/// save.
/// <list type="bullet">
/// <item>Create needs <c>sales.contracts.write</c>.</item>
/// <item>Approve and reject need <c>sales.contracts.discount.approve</c> and the expected version; the creator never decides
/// their own contract's discount.</item>
/// <item>Reads need <c>sales.contracts.read</c>.</item>
/// </list>
/// </summary>
public interface ISalesContractService
{
    Task<SalesContractCommandResult> CreateContractAsync(CreateSalesContractRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<SalesContractCommandResult> ApproveContractDiscountAsync(Guid contractId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesContractCommandResult> RejectContractDiscountAsync(Guid contractId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesContractView> GetContractAsync(Guid contractId, CancellationToken cancellationToken = default);

    Task<SalesContractListResponse> GetContractsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// NS-13 / SAT-D346: the contract discount limit (a percentage, 0..100) the Settings owner publishes for the current company on
/// the given business date, read under the command's fence. Sinan enters the limit; it has no default and is never seeded.
/// <para>
/// Until a host registers this port, Sales reads no limit: the limit is not configured, so every discount goes to approval and the
/// screen says "sınır tanımlı değil" — never "unlimited". A null answer means the same. An owner that cannot answer throws
/// <see cref="SalesContractDiscountLimitSourceException"/>: denied → 403, otherwise 503 (<c>sales_discount_limit_unavailable</c>);
/// never a null answer. Whether this limit is the order discount limit (SAT-D2) or a separate contract setting, and its scope
/// (company or group), is NEEDS-SINAN; the port keeps the decision outside Sales.
/// </para>
/// </summary>
public interface ISalesContractDiscountLimitSourceV1
{
    Task<decimal?> GetContractDiscountLimitPercentAsync(DateOnly effectiveOn, CancellationToken cancellationToken = default);
}

/// <summary>The contract discount limit owner could not answer: <see cref="IsDenied"/> (403) or unavailable (503).</summary>
public sealed class SalesContractDiscountLimitSourceException(bool isDenied)
    : Exception(isDenied ? "sales_contract_discount_limit_denied" : "sales_contract_discount_limit_unavailable")
{
    public bool IsDenied { get; } = isDenied;
}
