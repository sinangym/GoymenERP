using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// S-11 — the sales contract record (SAT-D28 EK3; NS-13, SAT-D346). The K3 skeleton: the operation's own name is measured before
/// anything is read, after every awaited read and right before the single save; the replay comes before any read; one save writes
/// the contract, its attempt and the audit event; a terminal refusal of a decision (stale version, state conflict, self-decision)
/// is recorded under the key and answered again on retry.
/// <list type="bullet">
/// <item><b>Create</b> (<c>sales.contracts.write</c>): the customer (party + company account, active, this company) is the
/// Counterparty owner's answer; the currency's money profile is the Settings owner's for today's Europe/Istanbul business date; the
/// contract discount limit is read through <see cref="ISalesContractDiscountLimitSourceV1"/>. Without that port, or when it
/// answers null, no limit is configured: every discount goes to approval (SAT-D346) — never "unlimited". SAT-D358: the limit is a
/// separate per-company Settings value whose provider the Settings owner implements; until it does, the unset limit holds.
/// SAT-D362: the end date is optional (null = open-ended); SAT-D368 (SINAN-CEVAP 33) confirms it.</item>
/// <item><b>Approve / reject the discount</b> (<c>sales.contracts.discount.approve</c>, NS-13): the expected version, only a
/// pending contract, never by its creator; a rejection takes a reason.</item>
/// <item><b>Read</b> (<c>sales.contracts.read</c>): the company's contracts, newest first, and one contract.</item>
/// <item><b>Amounts</b> (SAT-D361): every answer asks <c>sales.orders.price.read</c> — the sales order price read, the same name and
/// the same company-wide measurement the order and invoice reads use — at the moment of answering. Granted → the installment amounts
/// and the planned total are disclosed (<c>AmountDisclosure: Visible</c>); otherwise they are null beside
/// <c>AmountDisclosure: Withheld</c>, server-side. The price read is asked, never required: its absence never refuses the call.</item>
/// </list>
/// The contract is a separate record: no order, quote or price is read or changed. The agreement price tier (SAT-D58) is not
/// evaluated here.
/// </summary>
public sealed class SalesContractService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    ISalesContractStore contracts,
    ISalesPartyAccountPortV1 accounts,
    ISettingsPrerequisiteProvider settings,
    TimeProvider timeProvider,
    ISalesContractDiscountLimitSourceV1? limits = null) : ISalesContractService
{
    private const string WritePermission = SalesPermissionManifestV1.ContractsWrite;
    private const string ApprovePermission = SalesPermissionManifestV1.ContractsDiscountApprove;
    private const string ReadPermission = SalesPermissionManifestV1.ContractsRead;

    /// <summary>SAT-D361, SAT-D369 (SINAN-CEVAP 33): the sales order price read (SALES-P2) also governs the contract's amounts.</summary>
    private const string PriceReadPermission = SalesPermissionManifestV1.OrdersPriceRead;

    private static readonly SalesDraftStateAudit CreatedAudit =
        new("sales.contract.created.v1", "CreateContract", "Satış sözleşmesi kaydedildi");

    private static readonly SalesDraftStateAudit ApprovedAudit =
        new("sales.contract.discount-approved.v1", "ApproveContractDiscount", "Sözleşme iskontosu onaylandı");

    private static readonly SalesDraftStateAudit RejectedAudit =
        new("sales.contract.discount-rejected.v1", "RejectContractDiscount", "Sözleşme iskontosu reddedildi");

    public async Task<SalesContractCommandResult> CreateContractAsync(CreateSalesContractRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(WritePermission, cancellationToken);
        try
        {
            SalesContractJson.Validate(request);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }

        var requestHash = SalesContractHash.Compute(request);
        var envelope = Envelope(scope, WritePermission, metadata);
        var replay = await FindReplayAsync(scope, WritePermission, envelope, requestHash, SalesContractCommands.CreateV1,
            cancellationToken);
        if (replay is not null)
        {
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        await RequireAccountAsync(scope, request.PartyId, request.CompanyAccountId, cancellationToken);
        var today = Day(timeProvider.GetUtcNow());
        var money = await ReadMoneyProfileAsync(scope, request.CurrencyId, today, cancellationToken);
        var limit = await ReadDiscountLimitAsync(scope, today, cancellationToken);
        var installments = request.Installments
            .Select(item => new SalesContractInstallmentInput(item.DueDate, SalesContractJson.ParseDecimal(item.Amount, positive: true)))
            .ToArray();
        var rules = (request.DiscountRules ?? [])
            .Select(rule => new SalesContractDiscountRuleInput(Kind(rule.Kind), rule.Label,
                SalesContractJson.ParseDecimal(rule.RatePercent, positive: true)))
            .ToArray();

        var contractId = Guid.NewGuid();
        await RequireStillAsync(scope, WritePermission, cancellationToken);
        SalesContractStoreResult stored;
        try
        {
            stored = await contracts.StoreContractAsync(envelope, requestHash,
                (contractNo, now) => SalesContract.Create(contractId, scope.CompanyId, contractNo, request.PartyId,
                    request.CompanyAccountId, money, request.ValidFrom, request.ValidThrough, installments, rules, request.Clauses,
                    limit, scope.ActorId, now),
                CreatedAudit, cancellationToken);
        }
        catch (SalesRuleException exception) when (CreateField(exception.Code) is { } field)
        {
            throw new SalesDraftServiceException(422, exception.Code, [new(field, exception.Code)]);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("contract", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: a racing caller of the same key answers the winner; a lost number race is the caller's concurrent_change.
            var won = await FindReplayAsync(scope, WritePermission, envelope, requestHash, SalesContractCommands.CreateV1,
                cancellationToken);
            return won is null ? throw Store(exception) : await ReplayedAsync(scope, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        // No fence after the commit (S03): the stored contract is answered; the next request of the key meets the fence first.
        return await ResultAsync(scope, stored, stored.Outcome == SalesDraftWriteOutcome.Replayed, cancellationToken);
    }

    public Task<SalesContractCommandResult> ApproveContractDiscountAsync(Guid contractId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideAsync(contractId, request, SalesContractCommands.ApproveDiscountV1, metadata,
            (target, actor, version, now) => target.ApproveDiscount(version, actor, now), ApprovedAudit, cancellationToken);

    public Task<SalesContractCommandResult> RejectContractDiscountAsync(Guid contractId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideAsync(contractId, request, SalesContractCommands.RejectDiscountV1, metadata,
            (target, actor, version, now) => target.RejectDiscount(version, actor, request!.Reason, now), RejectedAudit,
            cancellationToken);

    public async Task<SalesContractView> GetContractAsync(Guid contractId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ReadPermission, cancellationToken);
        if (contractId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        // Another company's contract is null under the tenant filter: 404.
        var contract = await StoreAsync(scope, ReadPermission, () => contracts.FindContractAsync(contractId, cancellationToken),
            cancellationToken) ?? throw new SalesDraftServiceException(404, "not_found");
        var disclosure = await DiscloseAmountsAsync(scope, cancellationToken);
        return View(contract, Day(timeProvider.GetUtcNow()), disclosure);
    }

    public async Task<SalesContractListResponse> GetContractsAsync(CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ReadPermission, cancellationToken);
        var items = await StoreAsync(scope, ReadPermission,
            () => contracts.ListContractsAsync(SalesContractApiV1.MaximumListed, cancellationToken), cancellationToken);
        var today = Day(timeProvider.GetUtcNow());
        var disclosure = await DiscloseAmountsAsync(scope, cancellationToken);
        return new SalesContractListResponse([.. items.Select(item => View(item, today, disclosure))]);
    }

    private async Task<SalesContractCommandResult> DecideAsync(Guid contractId, SalesDraftStateCommandRequest? request,
        string commandName, SalesCommandMetadata metadata, Action<SalesContract, string, long, DateTimeOffset> decide,
        SalesDraftStateAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(ApprovePermission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion)
            || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        var requestHash = SalesContractHash.Compute(commandName, contractId, request);
        var envelope = Envelope(scope, ApprovePermission, metadata);
        var replay = await FindReplayAsync(scope, ApprovePermission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (contractId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, ApprovePermission, cancellationToken);
        SalesContractStoreResult? result;
        try
        {
            result = await contracts.DecideContractAsync(envelope, requestHash, commandName, contractId,
                (target, now) => decide(target, scope.ActorId, expectedVersion, now), audit, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            return await TerminalAsync(scope, envelope, requestHash, commandName, contractId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            return await TerminalAsync(scope, envelope, requestHash, commandName, contractId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.ContractSelfDecision)
        {
            // Maker≠checker: the creator decides their own contract's discount; the same actor under the same key stays refused.
            return await TerminalAsync(scope, envelope, requestHash, commandName, contractId,
                new SalesDraftTerminalRefusal(409, SalesContractProblemCodesV1.SelfDecision, [new("actor", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.ContractReasonTooLong)
        {
            throw new SalesDraftServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("reason", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: a lost same-key race answers the winner; a lost version race is the caller's concurrent_change.
            var won = await FindReplayAsync(scope, ApprovePermission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : await ReplayedAsync(scope, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new SalesDraftServiceException(404, "not_found")
            : await ResultAsync(scope, result, result.Outcome == SalesDraftWriteOutcome.Replayed, cancellationToken);
    }

    /// <summary>The field each create rule names on the wire; null for a rule that is not the create's own (generic 422).</summary>
    private static string? CreateField(string code) => code switch
    {
        SalesRuleCodes.MoneyProfileNotInEffect or SalesRuleCodes.SnapshotOperationMismatch => "currencyId",
        SalesRuleCodes.ContractValidityInvalid => "validThrough",
        SalesRuleCodes.ContractInstallmentsInvalid => "installments",
        SalesRuleCodes.ContractDiscountInvalid => "discountRules",
        SalesRuleCodes.ContractClausesTooLong => "clauses",
        _ => null,
    };

    private static SalesContractDiscountKind Kind(string text) =>
        SalesContractJson.TryKind(text, out var kind) ? kind : throw new SalesDraftServiceException(400, "validation_failed");

    // ---- owners -------------------------------------------------------------------------------------------------------------

    /// <summary>The Counterparty owner's answer for the customer (party + account of that party + this company + both active).</summary>
    private async Task RequireAccountAsync(Scope scope, Guid partyId, Guid companyAccountId, CancellationToken cancellationToken)
    {
        var answer = await GuardedAsync(scope, WritePermission,
            () => accounts.GetAuthorizedAsync(partyId, companyAccountId, cancellationToken), cancellationToken);
        switch (answer.Outcome)
        {
            case SalesPartyAccountOutcomeV1.Found when answer.Account is { } account:
                if (account.PartyId != partyId || account.CompanyAccountId != companyAccountId)
                {
                    throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
                }

                if (account.CompanyId != scope.CompanyId)
                {
                    throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.AccountState,
                        [new("companyAccountId", "company_mismatch")]);
                }

                if (!account.PartyIsActive || !account.AccountIsActive)
                {
                    throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.AccountState,
                        [new("companyAccountId", "passive_company_account")]);
                }

                return;
            case SalesPartyAccountOutcomeV1.NotFound:
                throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.AccountState,
                    [new("partyId", "invalid_reference"), new("companyAccountId", "invalid_reference")]);
            case SalesPartyAccountOutcomeV1.Denied:
                throw new SalesDraftServiceException(403, "forbidden");
            default:
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);
        }
    }

    /// <summary>
    /// The Settings owner's money profile of the contract currency for today's business date. The installment amounts are checked
    /// against its scale by the domain (never rounded). Validity (active, in effect) is the domain's rule.
    /// </summary>
    private async Task<MoneyProfileSnapshot> ReadMoneyProfileAsync(Scope scope, Guid currencyId, DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        MoneyProfileV1 profile;
        try
        {
            profile = await GuardedAsync(scope, WritePermission,
                () => settings.GetMoneyProfileAsync(currencyId, SalesMoneyOperations.OrderDraftV1, businessDate, cancellationToken),
                cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw exception.Code switch
            {
                "settings_access_denied" => new SalesDraftServiceException(403, "settings_access_denied"),
                _ when exception.Code == SettingsPrerequisiteCodesV1.MoneyProfileNotFound =>
                    new SalesDraftServiceException(422, SalesDraftProblemCodesV1.UnsupportedCurrency,
                        [new("currencyId", SalesDraftProblemCodesV1.UnsupportedCurrency)]),
                _ => new SalesDraftServiceException(503, "settings_provider_unavailable"),
            };
        }

        if (profile.CompanyId != scope.CompanyId || profile.CurrencyId != currencyId
            || !string.Equals(profile.Operation, SalesMoneyOperations.OrderDraftV1, StringComparison.Ordinal)
            || profile.Profile.ProfileId == Guid.Empty || profile.Profile.Version <= 0 || profile.Scale < 0)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        return new MoneyProfileSnapshot(profile.CompanyId, profile.CurrencyId, profile.Operation, profile.Profile.ProfileId,
            profile.Profile.Version.ToString(CultureInfo.InvariantCulture), businessDate, profile.IsActive,
            profile.EffectiveFrom, profile.EffectiveThrough, profile.Scale);
    }

    /// <summary>
    /// NS-13 / SAT-D346: the configured contract discount limit for today. No port or a null answer is "not configured" (every
    /// discount goes to approval). A denied owner is 403; an owner that cannot answer, or answers outside 0..100 or with more
    /// decimals than a rate carries, is 503 <c>sales_discount_limit_unavailable</c> (nothing is guessed).
    /// </summary>
    private async Task<decimal?> ReadDiscountLimitAsync(Scope scope, DateOnly businessDate, CancellationToken cancellationToken)
    {
        if (limits is null)
        {
            return null;
        }

        decimal? limit;
        try
        {
            limit = await GuardedAsync(scope, WritePermission,
                () => limits.GetContractDiscountLimitPercentAsync(businessDate, cancellationToken), cancellationToken);
        }
        catch (SalesContractDiscountLimitSourceException exception)
        {
            throw exception.IsDenied
                ? new SalesDraftServiceException(403, "settings_access_denied")
                : new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DiscountLimitUnavailable);
        }

        if (limit is { } value
            && (value < 0m || value > SalesContract.MaximumRatePercent
                || Money.SignificantDecimals(value) > SalesContract.RateScale))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DiscountLimitUnavailable);
        }

        return limit;
    }

    // ---- replay ---------------------------------------------------------------------------------------------------------------

    private SalesDraftWriteEnvelope Envelope(Scope scope, string permission, SalesCommandMetadata metadata) =>
        new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

    private async Task<SalesContractReplay?> FindReplayAsync(Scope scope, string permission, SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken) =>
        await StoreAsync(scope, permission,
            () => contracts.FindContractReplayAsync(envelope, requestHash, commandName, cancellationToken), cancellationToken);

    /// <summary>ADR-011 §4/§7: the stored contract as the replay, or the recorded refusal as the same refusal.</summary>
    private async Task<SalesContractCommandResult> ReplayedAsync(Scope scope, SalesContractReplay replay,
        CancellationToken cancellationToken)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                originalCorrelationId: replay.OriginalCorrelationId);
        }

        var disclosure = await DiscloseAmountsAsync(scope, cancellationToken);
        return new SalesContractCommandResult(View(replay.Contract!, Day(timeProvider.GetUtcNow()), disclosure), true,
            replay.OriginalCorrelationId);
    }

    /// <summary>ADR-011 §7: the refusal is recorded under the key and then answered; a lost record race answers the winner.</summary>
    private async Task<SalesContractCommandResult> TerminalAsync(Scope scope, SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid contractId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken)
    {
        var permission = envelope.PermissionCode;
        await RequireStillAsync(scope, permission, cancellationToken);
        try
        {
            await contracts.RecordContractRefusalAsync(envelope, requestHash, commandName, contractId, refusal, cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won is not null)
            {
                return await ReplayedAsync(scope, won, cancellationToken);
            }
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    private async Task<SalesContractCommandResult> ResultAsync(Scope scope, SalesContractStoreResult stored, bool isReplay,
        CancellationToken cancellationToken)
    {
        var disclosure = await DiscloseAmountsAsync(scope, cancellationToken);
        return new(View(stored.Contract, Day(timeProvider.GetUtcNow()), disclosure), isReplay, stored.OriginalCorrelationId);
    }

    /// <summary>
    /// SAT-D361: the sales order price read ASKED, never required — the operation's own name was required before this, exactly as
    /// the order (SALES-P2) and invoice (SAT-D238) reads ask it. Unregistered → 503 (a setup gap, never a grant); granted
    /// company-wide for this actor in this company → <c>Visible</c>; anything else → <c>Withheld</c>. Asked last, at the moment
    /// of answering, so the disclosure is the authority's freshest word.
    /// </summary>
    private async Task<SalesPriceDisclosure> DiscloseAmountsAsync(Scope scope, CancellationToken cancellationToken)
    {
        if (!ErpPermissions.IsKnown(PriceReadPermission))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PermissionUnregistered);
        }

        var read = await authority.RevalidatePermissionScopeAsync(PriceReadPermission, scope.CompanyId, cancellationToken);
        return read.IsGranted && read.IsCompanyWide && Matches(scope.CompanyId, scope.ActorId)
            ? SalesPriceDisclosure.Visible
            : SalesPriceDisclosure.Withheld;
    }

    // ---- mapping --------------------------------------------------------------------------------------------------------------

    /// <summary>SAT-D361: a withheld contract carries no amount at all (null beside <c>Withheld</c>), never a zero.</summary>
    internal static SalesContractView View(SalesContract item, DateOnly today, SalesPriceDisclosure disclosure)
    {
        var visible = disclosure == SalesPriceDisclosure.Visible;
        return new(
        item.Id,
        item.ContractNo,
        item.PartyId,
        item.CompanyAccountId,
        item.CurrencyId,
        item.MoneyScale,
        item.State.ToString(),
        item.Version.ToString(CultureInfo.InvariantCulture),
        item.ValidFrom,
        item.ValidThrough,
        visible ? SalesPriceDisclosure.Visible : SalesPriceDisclosure.Withheld,
        [.. item.Installments.Select(installment => new SalesContractInstallmentView(installment.InstallmentNo, installment.DueDate,
            visible ? Amount(installment.Amount.Amount, item.MoneyScale) : null))],
        visible ? Amount(item.PlannedTotal.Amount, item.MoneyScale) : null,
        [.. item.DiscountRules.Select(rule => new SalesContractDiscountRuleView(rule.RuleNo, rule.Kind.ToString(), rule.Label, Rate(rule.RatePercent)))],
        item.Clauses,
        item.DiscountApprovalRequired,
        item.DiscountLimitPercent is not null,
        item.DiscountLimitPercent is { } limit ? Rate(limit) : null,
        item.IsInEffectOn(today),
        item.CreatedBy,
        item.CreatedAtUtc,
        item.Decision?.DecidedBy,
        item.Decision?.DecidedAtUtc,
        item.Decision?.RejectionReason);
    }

    /// <summary>The exact amount at the money profile's scale as invariant decimal text (never a JSON number, never rounded).</summary>
    private static string Amount(decimal amount, int scale) =>
        amount.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>A rate as invariant decimal text without trailing zeros (at most <see cref="SalesContract.RateScale"/> decimals).</summary>
    private static string Rate(decimal rate) => rate.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>SAT-D10: the Europe/Istanbul day; without the zone nothing is guessed (503, never UTC).</summary>
    private static DateOnly Day(DateTimeOffset instant)
    {
        try
        {
            return SalesBusinessDay.DateOf(instant);
        }
        catch (SalesBusinessDayUnavailableException)
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }
    }

    // ---- fences -------------------------------------------------------------------------------------------------------------

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new SalesDraftServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (!ErpPermissions.IsKnown(permission))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private async Task RequireStillAsync(Scope scope, string permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.CanAccessCompany(companyId);

    /// <summary>An owner read is released only under the authority a success would need; a failure measures the fence first.</summary>
    private async Task<T> GuardedAsync<T>(Scope scope, string permission, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    /// <summary>A Sales store read under the fence: a store refusal is mapped; the fence is measured again after the await.</summary>
    private async Task<T> StoreAsync<T>(Scope scope, string permission, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (SalesDraftStoreException exception)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw Store(exception);
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    private static SalesDraftServiceException Store(SalesDraftStoreException exception) => exception.Code switch
    {
        SalesDraftStoreCodes.SameKeyPayloadMismatch => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch),
        SalesDraftStoreCodes.ConcurrentChange => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.ConcurrentChange),
        SalesDraftStoreCodes.CompanyContextMismatch => new SalesDraftServiceException(403, "forbidden"),
        SalesDraftStoreCodes.ValueDoesNotFit => new SalesDraftServiceException(422, "validation_failed", [new("contract", exception.Code)]),
        _ => new SalesDraftServiceException(503, "service_unavailable"),
    };

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}
