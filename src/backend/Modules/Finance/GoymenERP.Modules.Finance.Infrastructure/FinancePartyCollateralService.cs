using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// API-85b (F018 plan §L2; FNS-D57, FNS-D31, FNS-D58): the Finance customer collateral (müşteri teminatı) master. It
/// moves no money and writes no ledger.
/// <list type="bullet">
/// <item><b>One step (FNS-D57):</b> a holder of <c>finance.collaterals.record</c> records, changes or releases a received
/// collateral directly, effective at once: no proposer/approver, no threshold. Every command is audited with the old and
/// the new values.</item>
/// <item><b>Append-only revisions:</b> a record is revision 1; a change or a release is the next revision and supersedes the
/// previous one (<c>UX_PartyCollaterals_Live</c>: one effective revision per collateral; <c>UX_PartyCollaterals_Revision</c>:
/// two commands from the same revision cannot both win). A release is a reverse record repeating the previous values with
/// a reason, never a delete. Every change or release names the collateral's current revision.</item>
/// <item><b>Type and rate (FNS-D31):</b> the type is a Settings collateral type read through
/// <see cref="IFinanceCollateralTypeCatalogReader"/>: active, listed once, not a cheque/note class (refused here too), with
/// a rate Finance can read exactly within 0–100. The rate is never stored: it is applied at read time
/// (<see cref="FinancePartyCollateralFigures"/>). A catalog that cannot answer refuses as Unavailable (the default until
/// Settings registers its adapter).</item>
/// <item><b>Amount:</b> exact decimal text &gt; 0 in one Settings money currency candidate, stored as given, never rounded or
/// converted (the credit limit's exact amount rule, so the Sales risk port can answer it).</item>
/// <item><b>Party:</b> the (party, company account) pair is read through <see cref="IFinancePartyAccountPort"/>; a record or
/// a change needs an active account and party, a release does not. An account whose collaterals name another party is
/// refused rather than guessed.</item>
/// <item><b>Documentary risk (FNS-D58):</b> shown beside the collaterals and never added to them or to the gross risk; its
/// amounts follow the customer instrument read and amount authorities (otherwise Withheld).</item>
/// <item><b>Authority, idempotency (ADR-011) and audit (ADR-009):</b> as the credit limit master.</item>
/// </list>
/// Not built: a haircut (OPEN-010), the given direction's commands (API-84), a validity-expiry job (a collateral past its
/// end simply stops counting).
/// </summary>
public sealed class FinancePartyCollateralService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    ISettingsMoneyCandidateReader currencies,
    IFinancePartyAccountPort parties,
    IFinanceCollateralTypeCatalogReader collateralTypes,
    IAuditEventCollector auditEvents,
    FinancePartyCollateralEnvelopes envelopes,
    TimeProvider timeProvider) : IFinancePartyCollateralMaster
{
    public const string RecordCommand = "finance.collateral.record";
    public const string ChangeCommand = "finance.collateral.change";
    public const string ReleaseCommand = "finance.collateral.release";
    private const string EntityType = "Finance.PartyCollateral";
    internal const int Received = 1;
    internal const int Given = 2;
    internal const int ManualEntry = 1;
    internal const int RecordKind = 1;
    internal const int ChangeKind = 2;
    internal const int ReleaseKind = 3;
    internal const int Effective = 1;
    internal const int Superseded = 2;
    private const int IdentityMaximumLength = 128;
    private const int TypeVersionMaximumLength = 64;
    private const int DocumentReferenceMaximumLength = 128;
    private const int ReasonMaximumLength = 500;

    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<PartyCollateralCommandResult> RecordAsync(FinanceContext context, CommandMetadata metadata,
        PartyCollateralRecordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.RecordCollateral;
        if (await StartAsync(context, metadata, action, cancellationToken) is { } refused)
        {
            return refused;
        }

        // A null text cannot be the contract's JSON; refused before it is hashed.
        if (request.Amount is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("amount", "collateral_amount_invalid"));
        }

        var payloadHash = PayloadHash(RecordCommand, request);
        if (await PriorAsync(context, metadata, action, RecordCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (request.PartyId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("partyId", "identity_required"));
        }

        if (request.CompanyAccountId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("companyAccountId", "identity_required"));
        }

        if (PayloadRefusal(context, request.CollateralTypeId, request.CurrencyId, request.Amount, request.ValidFrom,
                request.ValidUntil, request.DocumentReference) is { } invalid)
        {
            return invalid;
        }

        var (type, typeRefused) = await TypeAsync(context, action, request.CollateralTypeId, cancellationToken);
        if (typeRefused is not null)
        {
            return typeRefused;
        }

        if (await CurrencyRefusalAsync(context, action, request.CurrencyId, cancellationToken) is { } currencyRefused)
        {
            return currencyRefused;
        }

        if (await PartyRefusalAsync(context, action, request.PartyId, request.CompanyAccountId, requireActive: true,
                cancellationToken) is { } partyRefused)
        {
            return partyRefused;
        }

        var otherParty = await dbContext.PartyCollaterals.AsNoTracking()
            .AnyAsync(row => row.CompanyAccountId == request.CompanyAccountId && row.PartyId != request.PartyId,
                cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        if (otherParty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("companyAccountId", "party_account_ineligible"));
        }

        var now = timeProvider.GetUtcNow();
        var row = new PartyCollateralRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            CollateralId = Guid.NewGuid(),
            Revision = 1,
            Direction = Received,
            SourceKind = ManualEntry,
            PartyId = request.PartyId,
            CompanyAccountId = request.CompanyAccountId,
            Kind = RecordKind,
            CollateralTypeId = request.CollateralTypeId,
            CollateralTypeVersion = type!.Version,
            CurrencyId = request.CurrencyId,
            AmountText = request.Amount,
            ValidFrom = request.ValidFrom,
            ValidUntil = request.ValidUntil,
            DocumentReference = request.DocumentReference,
            ReleaseReason = null,
            Status = Effective,
            Version = 1,
            RecordedBy = context.ActorId,
            RecordedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.PartyCollaterals.Add(row);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, RecordCommand, payloadHash, row.Id, result, now);
        Audit(context, metadata, row.Id, "finance.collateral.recorded.v1", "Record", versionBefore: null, row.Version,
            "{}", RecordAudit(row), "Finans müşteri teminatı kaydı (yetkili tek adımda; hemen geçerli; para etkisi yok)");
        return await SaveAsync(context, metadata, action, RecordCommand, payloadHash, result, cancellationToken);
    }

    public async Task<PartyCollateralCommandResult> ChangeAsync(FinanceContext context, CommandMetadata metadata,
        PartyCollateralChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.RecordCollateral;
        if (await StartAsync(context, metadata, action, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (request.Amount is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("amount", "collateral_amount_invalid"));
        }

        if (request.ExpectedRevision is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedRevision", "identity_required"));
        }

        var payloadHash = PayloadHash(ChangeCommand, request);
        if (await PriorAsync(context, metadata, action, ChangeCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (TargetRefusal(context, request.CollateralId, request.ExpectedRevision) is { } malformed)
        {
            return malformed;
        }

        if (PayloadRefusal(context, request.CollateralTypeId, request.CurrencyId, request.Amount, request.ValidFrom,
                request.ValidUntil, request.DocumentReference) is { } invalid)
        {
            return invalid;
        }

        var (current, targetRefused) = await TargetAsync(context, metadata, action, ChangeCommand, payloadHash,
            request.CollateralId, request.ExpectedRevision, cancellationToken);
        if (targetRefused is not null)
        {
            return targetRefused;
        }

        if (current!.CollateralTypeId == request.CollateralTypeId && current.CurrencyId == request.CurrencyId
            && string.Equals(current.AmountText, request.Amount, StringComparison.Ordinal)
            && current.ValidFrom == request.ValidFrom && current.ValidUntil == request.ValidUntil
            && string.Equals(current.DocumentReference, request.DocumentReference, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("request", "collateral_unchanged"));
        }

        var (type, typeRefused) = await TypeAsync(context, action, request.CollateralTypeId, cancellationToken);
        if (typeRefused is not null)
        {
            return typeRefused;
        }

        if (await CurrencyRefusalAsync(context, action, request.CurrencyId, cancellationToken) is { } currencyRefused)
        {
            return currencyRefused;
        }

        if (await PartyRefusalAsync(context, action, current.PartyId, current.CompanyAccountId, requireActive: true,
                cancellationToken) is { } partyRefused)
        {
            return partyRefused;
        }

        var now = timeProvider.GetUtcNow();
        var before = RecordAudit(current);
        var versionBefore = current.Version;
        Supersede(current, now);
        var row = NextRevision(context, current, ChangeKind, request.CollateralTypeId, type!.Version, request.CurrencyId,
            request.Amount, request.ValidFrom, request.ValidUntil, request.DocumentReference, releaseReason: null, now);
        dbContext.PartyCollaterals.Add(row);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ChangeCommand, payloadHash, row.Id, result, now);
        Audit(context, metadata, row.Id, "finance.collateral.changed.v1", "Change", versionBefore, row.Version, before,
            RecordAudit(row),
            "Finans müşteri teminatı değişikliği (yetkili tek adımda; önceki sürümün yerine geçer; para etkisi yok)");
        return await SaveAsync(context, metadata, action, ChangeCommand, payloadHash, result, cancellationToken);
    }

    public async Task<PartyCollateralCommandResult> ReleaseAsync(FinanceContext context, CommandMetadata metadata,
        PartyCollateralReleaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.RecordCollateral;
        if (await StartAsync(context, metadata, action, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (request.ExpectedRevision is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedRevision", "identity_required"));
        }

        if (!IsCanonical(request.Reason))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "reason_invalid"));
        }

        if (request.Reason.Length > ReasonMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "finance_value_does_not_fit"));
        }

        var payloadHash = PayloadHash(ReleaseCommand, request);
        if (await PriorAsync(context, metadata, action, ReleaseCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (TargetRefusal(context, request.CollateralId, request.ExpectedRevision) is { } malformed)
        {
            return malformed;
        }

        var (current, targetRefused) = await TargetAsync(context, metadata, action, ReleaseCommand, payloadHash,
            request.CollateralId, request.ExpectedRevision, cancellationToken);
        if (targetRefused is not null)
        {
            return targetRefused;
        }

        if (await PartyRefusalAsync(context, action, current!.PartyId, current.CompanyAccountId, requireActive: false,
                cancellationToken) is { } partyRefused)
        {
            return partyRefused;
        }

        var now = timeProvider.GetUtcNow();
        var before = RecordAudit(current);
        var versionBefore = current.Version;
        Supersede(current, now);
        // The reverse record: the released values repeated, with the reason.
        var row = NextRevision(context, current, ReleaseKind, current.CollateralTypeId, current.CollateralTypeVersion,
            current.CurrencyId, current.AmountText, current.ValidFrom, current.ValidUntil, current.DocumentReference,
            request.Reason, now);
        dbContext.PartyCollaterals.Add(row);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ReleaseCommand, payloadHash, row.Id, result, now);
        Audit(context, metadata, row.Id, "finance.collateral.released.v1", "Release", versionBefore, row.Version, before,
            RecordAudit(row), "Finans müşteri teminatı çözümü (ters kayıt; yetkili tek adımda; para etkisi yok): "
            + request.Reason);
        return await SaveAsync(context, metadata, action, ReleaseCommand, payloadHash, result, cancellationToken);
    }

    /// <summary>
    /// The account's held received collaterals, the weighted collateral (read-time Settings rate) and the documentary
    /// risk. An account with no collateral is still answered (an empty list and a recorded zero), so the documentary risk
    /// is shown; nothing about the account itself is disclosed beyond Finance's own rows.
    /// </summary>
    public async Task<ReadResult<PartyCollateralAccountView>> GetAccountAsync(FinanceContext context,
        Guid companyAccountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, cancellationToken) is { } outcome)
        {
            return new ReadResult<PartyCollateralAccountView>(outcome, null);
        }

        if (companyAccountId == Guid.Empty)
        {
            return new ReadResult<PartyCollateralAccountView>(ReadOutcome.NotFound, null);
        }

        var held = await HeldReceivedAsync(dbContext, context.CompanyId, companyAccountId, cancellationToken);
        var asOf = ReadDate(timeProvider);
        var inputs = held.Select(HeldInput).ToList();
        var catalog = inputs.Any(input => FinancePartyCollateralFigures.IsInForce(input.ValidFrom, input.ValidUntil, asOf))
            ? await collateralTypes.ListAsync(context.CompanyId, asOf, cancellationToken)
            : null;
        var weighted = held.Select(row => row.PartyId).Distinct().Count() > 1
            ? FinancePartyCollateralFigures.Figure.Unavailable(FinancePartyCollateralFigures.CollateralUnreadable)
            : FinancePartyCollateralFigures.Weigh(inputs, catalog, asOf);
        var documentary = await DocumentaryRiskAsync(context, companyAccountId, cancellationToken);
        if (await ReadFenceAsync(context, cancellationToken) is { } again)
        {
            return new ReadResult<PartyCollateralAccountView>(again, null);
        }

        return new ReadResult<PartyCollateralAccountView>(ReadOutcome.Found, new PartyCollateralAccountView(
            context.CompanyId, companyAccountId,
            [.. held.OrderBy(row => row.RecordedAtUtc).ThenBy(row => row.CollateralId).Select(View)],
            FigureView(weighted), documentary));
    }

    public async Task<ReadResult<PartyCollateralHistoryPage>> HistoryAsync(FinanceContext context, Guid companyAccountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, cancellationToken) is { } outcome)
        {
            return new ReadResult<PartyCollateralHistoryPage>(outcome, null);
        }

        List<PartyCollateralRow> rows = companyAccountId == Guid.Empty
            ? []
            : await dbContext.PartyCollaterals.AsNoTracking()
                .Where(row => row.CompanyAccountId == companyAccountId && row.Direction == Received)
                .OrderBy(row => row.RecordedAtUtc).ThenBy(row => row.CollateralId).ThenBy(row => row.Revision)
                .ToListAsync(cancellationToken);
        if (await ReadFenceAsync(context, cancellationToken) is { } again)
        {
            return new ReadResult<PartyCollateralHistoryPage>(again, null);
        }

        return rows.Count == 0
            ? new ReadResult<PartyCollateralHistoryPage>(ReadOutcome.NotFound, null)
            : new ReadResult<PartyCollateralHistoryPage>(ReadOutcome.Found,
                new PartyCollateralHistoryPage(context.CompanyId, companyAccountId, [.. rows.Select(View)]));
    }

    /// <summary>
    /// Settings' active collateral types under the record authority: a cheque/note class and a type whose rate Finance
    /// cannot read are left out (they would be refused on record); otherwise as given, in Settings' order.
    /// </summary>
    public async Task<ReadResult<PartyCollateralTypeCandidates>> TypeCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await RecordReadFenceAsync(context, cancellationToken) is { } refused)
        {
            return new ReadResult<PartyCollateralTypeCandidates>(refused, null);
        }

        var catalog = await collateralTypes.ListAsync(context.CompanyId, ReadDate(timeProvider), cancellationToken);
        if (await RecordReadFenceAsync(context, cancellationToken) is { } fenced)
        {
            return new ReadResult<PartyCollateralTypeCandidates>(fenced, null);
        }

        if (catalog is not { State: FinanceCollateralTypeCatalogState.Available, Items: { } items }
            || items.Any(item => item is null))
        {
            return new ReadResult<PartyCollateralTypeCandidates>(ReadOutcome.Unavailable, null);
        }

        return new ReadResult<PartyCollateralTypeCandidates>(ReadOutcome.Found, new PartyCollateralTypeCandidates(
            context.CompanyId,
            [.. items.Where(IsCandidate).Select(item => new PartyCollateralTypeCandidate(item.CollateralTypeId, item.Code,
                item.Name, item.RatePercent, item.Version))]));
    }

    /// <summary>Settings' money currency candidates under the record authority, passed through as given.</summary>
    public async Task<ReadResult<PartyCollateralCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await RecordReadFenceAsync(context, cancellationToken) is { } refused)
        {
            return new ReadResult<PartyCollateralCurrencyCandidates>(refused, null);
        }

        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await RecordReadFenceAsync(context, cancellationToken) is { } fenced)
            {
                return new ReadResult<PartyCollateralCurrencyCandidates>(fenced, null);
            }

            return new ReadResult<PartyCollateralCurrencyCandidates>(
                exception.Code == "settings_access_denied" ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }

        if (await RecordReadFenceAsync(context, cancellationToken) is { } afterSettings)
        {
            return new ReadResult<PartyCollateralCurrencyCandidates>(afterSettings, null);
        }

        return new ReadResult<PartyCollateralCurrencyCandidates>(ReadOutcome.Found, new PartyCollateralCurrencyCandidates(
            context.CompanyId,
            [.. candidates.Select(candidate => new PartyCollateralCurrencyCandidate(candidate.CurrencyId, candidate.Code,
                candidate.DisplayLabel))]));
    }

    /// <summary>The held received collaterals of an account: effective revisions that are not a release. Untracked.</summary>
    internal static Task<List<PartyCollateralRow>> HeldReceivedAsync(FinanceDbContext dbContext, Guid companyId,
        Guid companyAccountId, CancellationToken cancellationToken) =>
        dbContext.PartyCollaterals.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.CompanyAccountId == companyAccountId
                && row.Direction == Received && row.Status == Effective && row.Kind != ReleaseKind)
            .ToListAsync(cancellationToken);

    internal static FinancePartyCollateralFigures.HeldCollateral HeldInput(PartyCollateralRow row) =>
        new(row.CollateralTypeId, row.CurrencyId, row.AmountText, row.ValidFrom, row.ValidUntil);

    /// <summary>
    /// The date a collateral's validity and the Settings rate are read on: the UTC calendar date of the server clock
    /// (NEEDS-SINAN: a company business date/time zone would replace it once one is decided).
    /// </summary>
    internal static DateOnly ReadDate(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>The canonical content of a command: its name and the exact contract JSON of the request.</summary>
    internal static string PayloadHash<T>(string command, T request) =>
        FinancePartyCreditLimitService.PayloadHash(command, request);

    private static bool IsCandidate(FinanceCollateralTypeCatalogItem item) =>
        item.IsActive && !item.IsNegotiableInstrument && item.CollateralTypeId != Guid.Empty
        && FinancePartyCollateralFigures.TryRate(item.RatePercent, out _) && IsTypeVersion(item.Version);

    private static bool IsTypeVersion(string? version) =>
        IsCanonical(version) && version!.Length <= TypeVersionMaximumLength;

    /// <summary>FNS-D58: the account's documentary risk, Withheld unless the caller may read instruments and their amounts.</summary>
    private async Task<PartyCollateralFigureView> DocumentaryRiskAsync(FinanceContext context, Guid companyAccountId,
        CancellationToken cancellationToken)
    {
        if (await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstruments, null, cancellationToken)
                != PermissionDecision.Allowed
            || await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstrumentAmount, null, cancellationToken)
                != PermissionDecision.Allowed)
        {
            return new PartyCollateralFigureView(PartyCollateralFigureState.Withheld, [],
                FinancePartyCollateralFigures.DocumentaryRiskWithheld);
        }

        var instruments = await dbContext.CustomerInstruments.AsNoTracking()
            .Where(row => row.CompanyId == context.CompanyId && row.CompanyAccountId == companyAccountId)
            .Select(row => new FinancePartyCollateralFigures.PortfolioInstrument(row.Status, row.CurrencyId, row.AmountText))
            .ToListAsync(cancellationToken);
        return FigureView(FinancePartyCollateralFigures.DocumentaryRisk(instruments));
    }

    private static PartyCollateralFigureView FigureView(FinancePartyCollateralFigures.Figure figure) =>
        figure.IsAvailable
            ? new PartyCollateralFigureView(PartyCollateralFigureState.Available,
                [.. figure.Amounts.Select(amount => new PartyCollateralAmountView(amount.CurrencyId, amount.Amount))], null)
            : new PartyCollateralFigureView(PartyCollateralFigureState.Unavailable, [], figure.ReasonCode);

    private static PartyCollateralRow NextRevision(FinanceContext context, PartyCollateralRow current, int kind,
        Guid collateralTypeId, string collateralTypeVersion, Guid currencyId, string amount, DateOnly validFrom,
        DateOnly? validUntil, string? documentReference, string? releaseReason, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            CollateralId = current.CollateralId,
            Revision = checked(current.Revision + 1),
            Direction = current.Direction,
            SourceKind = current.SourceKind,
            PartyId = current.PartyId,
            CompanyAccountId = current.CompanyAccountId,
            Kind = kind,
            CollateralTypeId = collateralTypeId,
            CollateralTypeVersion = collateralTypeVersion,
            CurrencyId = currencyId,
            AmountText = amount,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            DocumentReference = documentReference,
            ReleaseReason = releaseReason,
            Status = Effective,
            Version = 1,
            RecordedBy = context.ActorId,
            RecordedAtUtc = now,
            UpdatedAtUtc = now,
        };

    private static void Supersede(PartyCollateralRow row, DateTimeOffset now)
    {
        row.Status = Superseded;
        row.SupersededAtUtc = now;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
    }

    /// <summary>
    /// The collateral a change or a release names (tracked): its effective received revision. Unknown is a validation
    /// refusal (nothing disclosed); a released collateral is final; a stale revision is a recorded terminal conflict.
    /// </summary>
    private async Task<(PartyCollateralRow? Row, PartyCollateralCommandResult? Refusal)> TargetAsync(
        FinanceContext context, CommandMetadata metadata, FinanceAction action, string command, string payloadHash,
        Guid collateralId, string expectedRevision, CancellationToken cancellationToken)
    {
        var row = await dbContext.PartyCollaterals
            .SingleOrDefaultAsync(row => row.CollateralId == collateralId && row.Status == Effective, cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            return (null, fenced);
        }

        if (row is null || row.Direction != Received)
        {
            return (null, Refused(context, CommandOutcome.ValidationFailed, Issue("collateralId", "collateral_not_found")));
        }

        if (row.Kind == ReleaseKind)
        {
            return (null, Refused(context, CommandOutcome.ValidationFailed, Issue("collateralId", "collateral_released")));
        }

        if (!string.Equals(VersionText(row.Revision), expectedRevision, StringComparison.Ordinal))
        {
            return (null, await TerminalAsync(context, metadata, action, command, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedRevision", "version_conflict")),
                cancellationToken));
        }

        return (row, null);
    }

    /// <summary>
    /// FNS-D31: the type through the Settings catalog on the read date, fenced again after it. Unavailable when the catalog
    /// cannot answer or its rate/version cannot be read exactly; a validation refusal when the type is not an active
    /// candidate or is a cheque/note class.
    /// </summary>
    private async Task<(FinanceCollateralTypeCatalogItem? Item, PartyCollateralCommandResult? Refusal)> TypeAsync(
        FinanceContext context, FinanceAction action, Guid collateralTypeId, CancellationToken cancellationToken)
    {
        var catalog = await collateralTypes.ListAsync(context.CompanyId, ReadDate(timeProvider), cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            return (null, fenced);
        }

        if (catalog is not { State: FinanceCollateralTypeCatalogState.Available, Items: { } items }
            || items.Any(item => item is null))
        {
            return (null, Refused(context, CommandOutcome.Unavailable,
                Issue("collateralTypeId", "collateral_type_catalog_unavailable")));
        }

        var matches = items.Where(item => item.CollateralTypeId == collateralTypeId).Take(2).ToList();
        if (matches.Count == 2)
        {
            return (null, Refused(context, CommandOutcome.Unavailable,
                Issue("collateralTypeId", "collateral_type_catalog_unavailable")));
        }

        if (matches.Count == 0 || !matches[0].IsActive)
        {
            return (null, Refused(context, CommandOutcome.ValidationFailed,
                Issue("collateralTypeId", "collateral_type_not_a_candidate")));
        }

        var type = matches[0];
        if (type.IsNegotiableInstrument)
        {
            // FNS-D31(2): a cheque or a promissory note is never a collateral type, whatever Settings lists.
            return (null, Refused(context, CommandOutcome.ValidationFailed,
                Issue("collateralTypeId", "collateral_type_is_instrument")));
        }

        return FinancePartyCollateralFigures.TryRate(type.RatePercent, out _) && IsTypeVersion(type.Version)
            ? (type, null)
            : (null, Refused(context, CommandOutcome.Unavailable,
                Issue("collateralTypeId", "collateral_type_rate_unavailable")));
    }

    private static PartyCollateralCommandResult? TargetRefusal(FinanceContext context, Guid collateralId,
        string expectedRevision)
    {
        // Malformed identities are refused after the replay lookup (never stored, so never a replay).
        if (collateralId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("collateralId", "identity_required"));
        }

        return IsCanonical(expectedRevision)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("expectedRevision", "identity_required"));
    }

    private static PartyCollateralCommandResult? PayloadRefusal(FinanceContext context, Guid collateralTypeId,
        Guid currencyId, string amount, DateOnly validFrom, DateOnly? validUntil, string? documentReference)
    {
        if (collateralTypeId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("collateralTypeId", "identity_required"));
        }

        if (currencyId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "identity_required"));
        }

        if (!FinancePartyCreditLimitService.IsExactAmount(amount))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("amount", "collateral_amount_invalid"));
        }

        if (validUntil is { } until && until < validFrom)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("validUntil", "collateral_validity_invalid"));
        }

        return documentReference is null
            || (IsCanonical(documentReference) && documentReference.Length <= DocumentReferenceMaximumLength)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed,
                Issue("documentReference", "collateral_document_reference_invalid"));
    }

    /// <summary>The (party, company account) pair through Counterparty's port, fenced again after it.</summary>
    private async Task<PartyCollateralCommandResult?> PartyRefusalAsync(FinanceContext context, FinanceAction action,
        Guid partyId, Guid companyAccountId, bool requireActive, CancellationToken cancellationToken)
    {
        var party = await parties.GetAuthorizedAsync(context, partyId, companyAccountId, cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        if (party.Outcome != ReadOutcome.Found || party.Resource is not { } resource)
        {
            return party.Outcome switch
            {
                ReadOutcome.Denied => Refused(context, CommandOutcome.Denied),
                ReadOutcome.NotFound => Refused(context, CommandOutcome.ValidationFailed,
                    Issue("companyAccountId", "party_account_ineligible")),
                _ => Refused(context, CommandOutcome.Unavailable, Issue("companyAccountId", "party_account_unavailable")),
            };
        }

        return resource.CompanyId != context.CompanyId || resource.PartyId != partyId
            || resource.CompanyAccountId != companyAccountId
            || (requireActive && (!resource.IsActive || !resource.IsPartyActive))
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("companyAccountId", "party_account_ineligible"))
            : null;
    }

    /// <summary>The currency must be one of Settings' candidates now; an unavailable source refuses.</summary>
    private async Task<PartyCollateralCommandResult?> CurrencyRefusalAsync(FinanceContext context, FinanceAction action,
        Guid currencyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, action, cancellationToken) is { } fenced)
            {
                return fenced;
            }

            return exception.Code == "settings_access_denied"
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("currencyId", exception.Code));
        }

        if (await FenceAsync(context, action, cancellationToken) is { } afterSettings)
        {
            return afterSettings;
        }

        return candidates.Any(candidate => candidate.CurrencyId == currencyId)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "currency_not_a_candidate"));
    }

    /// <summary>Context, the action's fresh authority, and the key and context storage limits.</summary>
    private async Task<PartyCollateralCommandResult?> StartAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, action, cancellationToken) is { } denied)
        {
            return denied;
        }

        var key = metadata.IdempotencyKey;
        if (!IsCanonical(key) || key.Length > IdentityMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));
        }

        return context.ActorId.Length > IdentityMaximumLength || context.CorrelationId.Length > IdentityMaximumLength
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("context", "finance_value_does_not_fit"))
            : null;
    }

    /// <summary>
    /// ADR-011: the same (command, key hash) again answers its stored result as a replay with its first correlation;
    /// another payload or actor is a conflict that discloses nothing. The action is measured afresh before the replay.
    /// </summary>
    private async Task<PartyCollateralCommandResult?> PriorAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.PartyCollateralCommands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (prior.PayloadHash != payloadHash || !string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }

        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        var stored = JsonSerializer.Deserialize<PartyCollateralCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        return stored with { IsReplay = true };
    }

    /// <summary>Company/actor still the request's, then a fresh decision on the action.</summary>
    private async Task<PartyCollateralCommandResult?> FenceAsync(FinanceContext context, FinanceAction action,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        var decision = await permissions.CheckAsync(context, action, null, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("permission", "finance_permission_mapping_unavailable"));
        }

        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private Task<ReadOutcome?> ReadFenceAsync(FinanceContext context, CancellationToken cancellationToken) =>
        ReadOutcomeFenceAsync(context, FinanceAction.ReadCollaterals, cancellationToken);

    private Task<ReadOutcome?> RecordReadFenceAsync(FinanceContext context, CancellationToken cancellationToken) =>
        ReadOutcomeFenceAsync(context, FinanceAction.RecordCollateral, cancellationToken);

    private async Task<ReadOutcome?> ReadOutcomeFenceAsync(FinanceContext context, FinanceAction action,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return ReadOutcome.Denied;
        }

        var decision = await permissions.CheckAsync(context, action, null, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable;
        }

        return SameContext(context) ? null : ReadOutcome.Denied;
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private void AddCommand(FinanceContext context, CommandMetadata metadata, string command, string payloadHash,
        Guid? targetId, PartyCollateralCommandResult result, DateTimeOffset now) =>
        dbContext.PartyCollateralCommands.Add(new PartyCollateralCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            PartyCollateralRowId = targetId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    /// <summary>
    /// One save, fenced again immediately before it. A lost race (the collateral's live revision or its next revision,
    /// or a row version) clears the tracker and reads the key afresh: a same-key winner is this answer too (ADR-011 §8);
    /// anything else is a <c>concurrent_change</c> the caller may retry after reading the account again.
    /// </summary>
    private async Task<PartyCollateralCommandResult> SaveAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, PartyCollateralCommandResult result,
        CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            dbContext.ChangeTracker.Clear();
            return fenced;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            if (await PriorAsync(context, metadata, action, command, payloadHash, cancellationToken) is { } prior)
            {
                return prior;
            }

            return Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }

        return result;
    }

    /// <summary>ADR-011 §7: an authorized terminal refusal is recorded alone (no change, no audit) and answered.</summary>
    private Task<PartyCollateralCommandResult> TerminalAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, Guid? targetId, PartyCollateralCommandResult refusal,
        CancellationToken cancellationToken)
    {
        // Nothing read for the decision may be saved with the refusal.
        dbContext.ChangeTracker.Clear();
        AddCommand(context, metadata, command, payloadHash, targetId, refusal, timeProvider.GetUtcNow());
        return SaveAsync(context, metadata, action, command, payloadHash, refusal, cancellationToken);
    }

    private void Audit(FinanceContext context, CommandMetadata metadata, Guid recordId, string eventType,
        string operation, long? versionBefore, long? versionAfter, string before, string after, string reason)
    {
        // FNS-D57: one step, no proposer/approver and no threshold; the context says so.
        var approval = JsonSerializer.Serialize(new { rule = "FNS-D57", recordedBy = context.ActorId, direct = true },
            AuditJson);
        var envelope = envelopes.Record;
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId,
            eventType,
            EntityType,
            recordId.ToString("D"),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            context.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            after,
            reason,
            approval,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            context.CorrelationId,
            context.CausationId,
            metadata.IdempotencyKey));
    }

    /// <summary>FNS-D57: who, when, type, amount, currency and validity of one revision (old beside new in the audit).</summary>
    private static string RecordAudit(PartyCollateralRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        row.CollateralId,
        revision = VersionText(row.Revision),
        version = VersionText(row.Version),
        direction = DirectionOf(row.Direction).ToString(),
        source = SourceOf(row.SourceKind).ToString(),
        kind = KindOf(row.Kind).ToString(),
        status = StatusOf(row.Status).ToString(),
        row.PartyId,
        row.CompanyAccountId,
        row.CollateralTypeId,
        collateralTypeVersion = row.CollateralTypeVersion,
        row.CurrencyId,
        amount = row.AmountText,
        row.ValidFrom,
        row.ValidUntil,
        row.DocumentReference,
        row.ReleaseReason,
        row.RecordedBy,
        row.RecordedAtUtc,
    }, AuditJson);

    private static PartyCollateralRecordView View(PartyCollateralRow row) =>
        new PartyCollateralRecordView(row.Id, row.CompanyId, row.CollateralId, VersionText(row.Revision),
            DirectionOf(row.Direction), SourceOf(row.SourceKind), row.PartyId, row.CompanyAccountId, KindOf(row.Kind),
            row.CollateralTypeId, row.CollateralTypeVersion, row.CurrencyId, row.AmountText, row.ValidFrom, row.ValidUntil,
            row.DocumentReference, row.ReleaseReason, StatusOf(row.Status), row.RecordedAtUtc, row.SupersededAtUtc,
            VersionText(row.Version));

    private static PartyCollateralDirection DirectionOf(int direction) => direction switch
    {
        Received => PartyCollateralDirection.Received,
        Given => PartyCollateralDirection.Given,
        _ => throw new InvalidOperationException("Teminat yönü tanınmıyor."),
    };

    private static PartyCollateralSource SourceOf(int source) => source switch
    {
        ManualEntry => PartyCollateralSource.FinanceManualEntry,
        _ => throw new InvalidOperationException("Teminat kaynağı tanınmıyor."),
    };

    private static PartyCollateralKind KindOf(int kind) => kind switch
    {
        RecordKind => PartyCollateralKind.Record,
        ChangeKind => PartyCollateralKind.Change,
        ReleaseKind => PartyCollateralKind.Release,
        _ => throw new InvalidOperationException("Teminat kayıt türü tanınmıyor."),
    };

    private static PartyCollateralStatus StatusOf(int status) => status switch
    {
        Effective => PartyCollateralStatus.Effective,
        Superseded => PartyCollateralStatus.Superseded,
        _ => throw new InvalidOperationException("Teminat kayıt durumu tanınmıyor."),
    };

    private static string VersionText(long version) => FinancePartyCreditLimitService.VersionText(version);

    private static PartyCollateralCommandResult Succeeded(FinanceContext context, PartyCollateralRow row) =>
        new PartyCollateralCommandResult(CommandOutcome.Succeeded, View(row), IsReplay: false, context.CorrelationId, []);

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static PartyCollateralCommandResult Refused(FinanceContext context, CommandOutcome outcome,
        params ValidationIssue[] issues) =>
        new PartyCollateralCommandResult(outcome, null, IsReplay: false, context.CorrelationId, issues);
}

/// <summary>The ADR-009 envelope of the collateral master's writes: the roles that granted <c>finance.collaterals.record</c>.</summary>
public sealed record FinancePartyCollateralEnvelopes(FinanceAuditEnvelope Record);
