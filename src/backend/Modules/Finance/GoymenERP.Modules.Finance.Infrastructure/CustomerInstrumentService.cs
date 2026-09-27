using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

/// <summary>The ADR-009 envelopes of the three portfolio commands, each with the roles of its own permission.</summary>
public sealed record CustomerInstrumentAuditEnvelopes(
    FinanceAuditEnvelope Receive,
    FinanceAuditEnvelope StatusChange,
    FinanceAuditEnvelope Collect);

/// <summary>
/// DEC-FIN-016 first release: the customer cheque / promissory note portfolio.
/// <list type="bullet">
/// <item><b>Receive</b> stores the instrument <c>InPortfolio</c> with its first movement: no money event, no receipt
/// source, no allocation — the customer's debt stays open until the instrument is collected.</item>
/// <item><b>Status change</b> moves it one allowed step (<see cref="CustomerInstrumentRules.Target"/>): to a Finance
/// bank account for collection and back, dishonoured (a cheque bounced, a note protested) or returned to the customer.
/// None of these has a money effect.</item>
/// <item><b>Collect</b> is the one money effect: in ONE save the instrument turns <c>Collected</c>, ONE recorded receipt
/// source (the manual bank receipt shape, so F-03 distributes it like any receipt) and ONE money event
/// (<see cref="MoneyEventKinds.CustomerInstrumentCollected"/>) at the face value are written, with the command record,
/// the movement and the audit events. The instrument names that source once (a unique index), so a replay, a stale
/// retry or a race never credits the customer twice. The owners are read again (bank account approved, usable and of
/// the instrument's currency; party account active; the Settings profile in effect, the face value within its scale),
/// and the period follows the collection date (K1, the host's occurredOn rule).</item>
/// </list>
/// Authority, idempotency and disclosure follow <see cref="ReceiptDraftService"/>: each command's narrow action is
/// measured before anything is read, after every owner read and right before the save, replay included; the same key,
/// payload and actor replays with its first correlation, anything else under the key is a conflict; a missing
/// instrument and a stale version are terminal (ADR-011 §7); the loser of a race reads the key (§8). Every amount —
/// the face value in a command result, a replay, the list and the detail — is shown only with a fresh
/// <c>ReadCustomerInstrumentAmount</c>.
/// </summary>
public sealed class CustomerInstrumentService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    IFinanceBankAccountReader banks,
    IFinancePartyAccountPort parties,
    ISettingsPrerequisiteProvider settings,
    IFinanceRecordPeriodRule periodRule,
    IAuditEventCollector auditEvents,
    FinanceReceiptDraftBinding binding,
    CustomerInstrumentAuditEnvelopes envelopes,
    TimeProvider timeProvider) : ICustomerInstrumentService
{
    public const string ReceiveCommand = "finance.customer-instrument.receive";
    public const string StatusChangeCommand = "finance.customer-instrument.status-change";
    public const string CollectCommand = "finance.customer-instrument.collect";

    private const string EntityType = "Finance.CustomerInstrument";
    private const string ReceiptEntityType = "Finance.ReceiptDraft";
    private const int IdentityMaximumLength = 128;
    internal const int ListPageSize = 50;

    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();
    private static readonly JsonSerializerOptions AuditJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<CustomerInstrumentCommandResult> ReceiveAsync(FinanceContext context, CommandMetadata metadata,
        CustomerInstrumentReceiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ReceiveCustomerInstrument;
        if (await StartAsync(context, metadata, action, null, cancellationToken) is { } refused)
        {
            return refused;
        }

        var payloadHash = PayloadHash(ReceiveCommand, request);
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        if (await PriorAsync(context, ReceiveCommand, action, keyHash, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (CustomerInstrumentRules.ValidateShape(request) is { Count: > 0 } shape)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. shape]);
        }

        if (string.IsNullOrWhiteSpace(binding.MoneyOperation))
        {
            return Refused(context, CommandOutcome.Unavailable, Issue("profile", "finance_operation_mapping_unavailable"));
        }

        var operation = binding.MoneyOperation;
        if (await PartyAsync(context, action, null, request.PartyId, request.CompanyAccountId, cancellationToken)
            is { Refusal: { } partyRefusal })
        {
            return partyRefusal;
        }

        var profile = await ProfileAsync(context, action, null, request.Money.CurrencyId, operation, request.ReceivedOn,
            cancellationToken);
        if (profile.Refusal is { } profileRefusal)
        {
            return profileRefusal;
        }

        if (CustomerInstrumentRules.ValidateMoney(request, context.CompanyId, profile.Profile, operation) is { Count: > 0 } money)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. money]);
        }

        // The workspace's "araç no mükerrer değil": the same kind, serial, drawer and bank is already in this company's
        // portfolio (whatever its status). Not terminal: a corrected request under the same key is measured again.
        if (await dbContext.CustomerInstruments.AsNoTracking().AnyAsync(row => row.Kind == (int)request.Kind
                && row.SerialNumber == request.SerialNumber && row.Drawer == request.Drawer
                && row.BankName == request.BankName, cancellationToken))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("serialNumber", CustomerInstrumentRules.Duplicate));
        }

        var reference = CustomerInstrumentRules.Reference(profile.Profile!);
        var now = timeProvider.GetUtcNow();
        var row = new CustomerInstrumentRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            Version = 1,
            Kind = (int)request.Kind,
            Status = (int)CustomerInstrumentStatus.InPortfolio,
            PartyId = request.PartyId,
            CompanyAccountId = request.CompanyAccountId,
            SerialNumber = request.SerialNumber,
            Drawer = request.Drawer,
            BankName = request.BankName,
            BranchName = request.BranchName,
            DueOn = request.DueOn,
            ReceivedOn = request.ReceivedOn,
            CurrencyId = request.Money.CurrencyId,
            AmountText = request.Money.Amount,
            ProfileId = reference.ProfileId,
            ProfileVersion = reference.Version,
            Description = request.Description,
            StatusChangedOn = request.ReceivedOn,
            ReceivedBy = context.ActorId,
            ReceivedAtUtc = now,
            UpdatedBy = context.ActorId,
            UpdatedAtUtc = now,
        };
        dbContext.CustomerInstruments.Add(row);
        dbContext.CustomerInstrumentMovements.Add(Movement(context, row, null, request.ReceivedOn, null, null, null, now));

        var amountDecision = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstrumentAmount, row.Id,
            cancellationToken);
        var result = Succeeded(context, row);
        AddCommand(context, ReceiveCommand, keyHash, payloadHash, row.Id, result, now);
        Audit(context, metadata, envelopes.Receive, EntityType, row.Id, "finance.customer-instrument.received.v1", "Receive",
            null, row.Version, "{}", InstrumentAudit(row),
            row.Kind == (int)CustomerInstrumentKind.Cheque
                ? "Müşteri çeki portföye alındı (para etkisi yok; müşteri borcu tahsilde kapanır)"
                : "Müşteri senedi portföye alındı (para etkisi yok; müşteri borcu tahsilde kapanır)");

        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } beforeSave)
        {
            dbContext.ChangeTracker.Clear();
            return beforeSave;
        }

        if (await SaveAsync(context, ReceiveCommand, action, keyHash, payloadHash, cancellationToken) is { } lost)
        {
            return lost;
        }

        return Disclose(result, amountDecision);
    }

    public async Task<CustomerInstrumentCommandResult> ChangeStatusAsync(FinanceContext context, CommandMetadata metadata,
        CustomerInstrumentStatusChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ChangeCustomerInstrumentStatus;
        if (await StartAsync(context, metadata, action, request.InstrumentId, cancellationToken) is { } refused)
        {
            return refused;
        }

        var payloadHash = PayloadHash(StatusChangeCommand, request);
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        if (await PriorAsync(context, StatusChangeCommand, action, keyHash, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (request.InstrumentId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExpectedVersion))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("instrumentId", "instrument_identity_invalid"));
        }

        if (!Enum.IsDefined(request.Transition))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("transition", CustomerInstrumentRules.TransitionInvalid));
        }

        if (request.Note is { } note && (note.Length == 0 || !string.Equals(note, note.Trim(), StringComparison.Ordinal)
                || note.Length > CustomerInstrumentRules.NoteMaximumLength))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("note", CustomerInstrumentRules.ValueDoesNotFit));
        }

        var row = await dbContext.CustomerInstruments.SingleOrDefaultAsync(candidate => candidate.Id == request.InstrumentId,
            cancellationToken);
        if (row is null)
        {
            return await TerminalAsync(context, StatusChangeCommand, action, keyHash, payloadHash, null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("instrumentId", CustomerInstrumentRules.NotFound)),
                cancellationToken);
        }

        if (!string.Equals(VersionText(row.Version), request.ExpectedVersion, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, StatusChangeCommand, action, keyHash, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        }

        var kind = (CustomerInstrumentKind)row.Kind;
        var from = (CustomerInstrumentStatus)row.Status;
        if (CustomerInstrumentRules.Target(kind, from, request.Transition) is not { } to)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("transition", CustomerInstrumentRules.TransitionNotAllowed));
        }

        if (request.OccurredOn < row.StatusChangedOn)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("occurredOn", CustomerInstrumentRules.DateBeforePrevious));
        }

        Guid? bankAfter = null;
        if (request.Transition == CustomerInstrumentTransition.DepositForCollection)
        {
            if (request.TreasuryAccountId is not { } treasury || treasury == Guid.Empty)
            {
                return Refused(context, CommandOutcome.ValidationFailed,
                    Issue("treasuryAccountId", CustomerInstrumentRules.TreasuryRequired));
            }

            if (await BankAsync(context, action, row, treasury, cancellationToken) is { Refusal: { } bankRefusal })
            {
                return bankRefusal;
            }

            bankAfter = treasury;
        }
        else if (request.TreasuryAccountId is not null)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("treasuryAccountId", CustomerInstrumentRules.TreasuryNotApplicable));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = row.Version;
        var before = InstrumentAudit(row);
        // The movement keeps the bank the step concerns: the new one on a deposit, the one it leaves otherwise.
        var movementBank = bankAfter ?? row.TreasuryAccountId;
        row.Status = (int)to;
        row.TreasuryAccountId = bankAfter;
        row.StatusChangedOn = request.OccurredOn;
        row.UpdatedBy = context.ActorId;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        dbContext.CustomerInstrumentMovements.Add(Movement(context, row, from, request.OccurredOn, movementBank, null,
            request.Note, now));

        var amountDecision = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstrumentAmount, row.Id,
            cancellationToken);
        var result = Succeeded(context, row);
        AddCommand(context, StatusChangeCommand, keyHash, payloadHash, row.Id, result, now);
        Audit(context, metadata, envelopes.StatusChange, EntityType, row.Id, "finance.customer-instrument.status-changed.v1",
            request.Transition.ToString(), versionBefore, row.Version, before, InstrumentAudit(row),
            $"Müşteri çeki/senedi durum değişikliği: {from} → {to} (para etkisi yok)");

        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } beforeSave)
        {
            dbContext.ChangeTracker.Clear();
            return beforeSave;
        }

        if (await SaveAsync(context, StatusChangeCommand, action, keyHash, payloadHash, cancellationToken) is { } lost)
        {
            return lost;
        }

        return Disclose(result, amountDecision);
    }

    public async Task<CustomerInstrumentCommandResult> CollectAsync(FinanceContext context, CommandMetadata metadata,
        CustomerInstrumentCollectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.CollectCustomerInstrument;
        if (await StartAsync(context, metadata, action, request.InstrumentId, cancellationToken) is { } refused)
        {
            return refused;
        }

        var payloadHash = PayloadHash(CollectCommand, request);
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        if (await PriorAsync(context, CollectCommand, action, keyHash, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (request.InstrumentId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExpectedVersion)
            || request.TreasuryAccountId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("instrumentId", "instrument_identity_invalid"));
        }

        if (string.IsNullOrWhiteSpace(binding.MoneyOperation))
        {
            return Refused(context, CommandOutcome.Unavailable, Issue("profile", "finance_operation_mapping_unavailable"));
        }

        var operation = binding.MoneyOperation;
        var row = await dbContext.CustomerInstruments.SingleOrDefaultAsync(candidate => candidate.Id == request.InstrumentId,
            cancellationToken);
        if (row is null)
        {
            return await TerminalAsync(context, CollectCommand, action, keyHash, payloadHash, null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("instrumentId", CustomerInstrumentRules.NotFound)),
                cancellationToken);
        }

        if (!string.Equals(VersionText(row.Version), request.ExpectedVersion, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, CollectCommand, action, keyHash, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        }

        var from = (CustomerInstrumentStatus)row.Status;
        if (!CustomerInstrumentRules.CanCollect(from))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("instrumentId", CustomerInstrumentRules.TransitionNotAllowed));
        }

        if (from == CustomerInstrumentStatus.AtBankForCollection && row.TreasuryAccountId != request.TreasuryAccountId)
        {
            // The bank it was given to collects it; another account would be a second, unexplained bank movement.
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("treasuryAccountId", CustomerInstrumentRules.BankMismatch));
        }

        if (request.CollectedOn < row.StatusChangedOn)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("collectedOn", CustomerInstrumentRules.DateBeforePrevious));
        }

        var bank = await BankAsync(context, action, row, request.TreasuryAccountId, cancellationToken);
        if (bank.Refusal is { } bankRefusal)
        {
            return bankRefusal;
        }

        var party = await PartyAsync(context, action, row.Id, row.PartyId, row.CompanyAccountId, cancellationToken);
        if (party.Refusal is { } partyRefusal)
        {
            return partyRefusal;
        }

        var profile = await ProfileAsync(context, action, row.Id, row.CurrencyId, operation, request.CollectedOn,
            cancellationToken);
        if (profile.Refusal is { } profileRefusal)
        {
            return profileRefusal;
        }

        // The recorded receipt this collection becomes, checked exactly as a manual receipt's Record checks its source
        // against the owners' CURRENT answers: approved and usable bank account of the instrument's currency, active
        // party account, the profile in effect on the collection date and the face value within its scale (never
        // rounded). An invalid profile carries no reference; the validator refuses it before using one.
        var reference = profile.Profile is { Profile: { } version } && version.ProfileId != Guid.Empty && version.Version > 0
            ? CustomerInstrumentRules.Reference(profile.Profile)
            : new ProfileReference(string.Empty, string.Empty);
        var preparation = new ReceiptPreparation(row.PartyId, row.CompanyAccountId, request.TreasuryAccountId,
            new MoneyText(row.AmountText, row.CurrencyId), reference, request.CollectedOn, request.ValueOn,
            row.SerialNumber, CollectionDescription(row));
        if (PreparationValidator.Validate(preparation, context.CompanyId, bank.Bank, party.Party, profile.Profile, operation)
            is { Count: > 0 } issues)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. issues]);
        }

        // Ö5 / K1: an OPEN period for the collection date, decided by the host's rule; a refusal lands before any write.
        var now = timeProvider.GetUtcNow();
        var period = await periodRule.CheckAsync(context, request.CollectedOn, request.ValueOn, now, cancellationToken);
        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } afterPeriod)
        {
            return afterPeriod;
        }

        switch (period.Outcome)
        {
            case RecordPeriodOutcome.Open:
                break;
            case RecordPeriodOutcome.NotOpen:
                return Refused(context, CommandOutcome.ValidationFailed,
                    Issue("collectedOn", period.Code ?? RecordPeriodAnswer.PeriodNotOpenCode));
            default:
                return Refused(context, CommandOutcome.ValidationFailed,
                    Issue("period", period.Code ?? RecordPeriodAnswer.RuleUnavailableCode));
        }

        var source = new ReceiptDraftRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            Version = 1,
            CreatedAtUtc = now,
            CreatedBy = context.ActorId,
            Stage = ReceiptStages.Recorded,
            RecordedAtUtc = now,
            RecordedBy = context.ActorId,
        };
        ReceiptDraftService.Apply(source, preparation, now, context.ActorId);
        dbContext.ReceiptDrafts.Add(source);
        dbContext.MoneyEvents.Add(new MoneyEventRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            SourceId = source.Id,
            Kind = MoneyEventKinds.CustomerInstrumentCollected,
            PartyId = source.PartyId,
            CompanyAccountId = source.CompanyAccountId,
            TreasuryAccountId = source.TreasuryAccountId,
            CurrencyId = source.CurrencyId,
            AmountText = source.AmountText,
            ProfileId = source.ProfileId,
            ProfileVersion = source.ProfileVersion,
            OccurredOn = source.OccurredOn,
            ValueOn = source.ValueOn,
            RecordedAtUtc = now,
            RecordedBy = context.ActorId,
            CorrelationId = context.CorrelationId,
        });

        var versionBefore = row.Version;
        var before = InstrumentAudit(row);
        row.Status = (int)CustomerInstrumentStatus.Collected;
        row.TreasuryAccountId = request.TreasuryAccountId;
        row.CollectionSourceId = source.Id;
        row.StatusChangedOn = request.CollectedOn;
        row.UpdatedBy = context.ActorId;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        dbContext.CustomerInstrumentMovements.Add(Movement(context, row, from, request.CollectedOn,
            request.TreasuryAccountId, source.Id, null, now));

        var amountDecision = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstrumentAmount, row.Id,
            cancellationToken);
        var result = Succeeded(context, row);
        AddCommand(context, CollectCommand, keyHash, payloadHash, row.Id, result, now);
        Audit(context, metadata, envelopes.Collect, EntityType, row.Id, "finance.customer-instrument.collected.v1",
            "Collect", versionBefore, row.Version, before, InstrumentAudit(row),
            "Müşteri çeki/senedi tahsil edildi (tek para olayı; müşteri nominal tutarla bir kez alacaklanır)");
        Audit(context, metadata, envelopes.Collect, ReceiptEntityType, source.Id, "finance.manual-receipt.recorded.v1",
            "Record", null, source.Version, "{}", CollectionSourceAudit(source, row),
            "Müşteri çeki/senedi tahsilatı kesin kaynak olarak kaydedildi (tek para olayı)");

        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } beforeSave)
        {
            dbContext.ChangeTracker.Clear();
            return beforeSave;
        }

        if (await SaveAsync(context, CollectCommand, action, keyHash, payloadHash, cancellationToken) is { } lost)
        {
            return lost;
        }

        return Disclose(result, amountDecision);
    }

    public async Task<ReadResult<CustomerInstrumentPage>> ListAuthorizedAsync(FinanceContext context, string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return new ReadResult<CustomerInstrumentPage>(ReadOutcome.Denied, null);
        }

        var read = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstruments, null, cancellationToken);
        if (read != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<CustomerInstrumentPage>(ReadRefusal(read), null);
        }

        var query = dbContext.CustomerInstruments.AsNoTracking();
        if (cursor is not null)
        {
            if (DraftListCursor.Decode(cursor, context.CompanyId, DraftListCursor.InstrumentOrdering) is not { } after)
            {
                return new ReadResult<CustomerInstrumentPage>(ReadOutcome.NotFound, null);
            }

            var afterReceivedAtUtc = after.CreatedAtUtc;
            var afterId = after.Id;
            query = query.Where(row => row.ReceivedAtUtc < afterReceivedAtUtc
                || (row.ReceivedAtUtc == afterReceivedAtUtc && row.Id.CompareTo(afterId) < 0));
        }

        var rows = await query
            .OrderByDescending(row => row.ReceivedAtUtc).ThenByDescending(row => row.Id)
            .Take(ListPageSize + 1)
            .ToListAsync(cancellationToken);
        var amount = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstrumentAmount, null, cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstruments, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<CustomerInstrumentPage>(ReadRefusal(again), null);
        }

        var page = rows.Take(ListPageSize).ToList();
        var next = rows.Count > ListPageSize
            ? DraftListCursor.Encode(context.CompanyId, page[^1].ReceivedAtUtc, page[^1].Id, DraftListCursor.InstrumentOrdering)
            : null;
        return new ReadResult<CustomerInstrumentPage>(ReadOutcome.Found, new CustomerInstrumentPage(context.CompanyId,
            [.. page.Select(row => View(row, Disclosure(amount)))], next));
    }

    public async Task<ReadResult<CustomerInstrumentDetail>> DetailAuthorizedAsync(FinanceContext context,
        Guid instrumentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return new ReadResult<CustomerInstrumentDetail>(ReadOutcome.Denied, null);
        }

        var read = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstruments, instrumentId,
            cancellationToken);
        if (read != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<CustomerInstrumentDetail>(ReadRefusal(read), null);
        }

        var row = await dbContext.CustomerInstruments.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == instrumentId, cancellationToken);
        var movements = row is null
            ? new List<CustomerInstrumentMovementRow>()
            : await dbContext.CustomerInstrumentMovements.AsNoTracking()
                .Where(movement => movement.InstrumentId == row.Id)
                .OrderBy(movement => movement.Version)
                .ToListAsync(cancellationToken);
        var amount = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstrumentAmount, instrumentId,
            cancellationToken);
        // What this actor may do NEXT: the state's steps, each only with its own fresh grant (never a guess).
        var mayChange = await permissions.CheckAsync(context, FinanceAction.ChangeCustomerInstrumentStatus, instrumentId,
            cancellationToken) == PermissionDecision.Allowed;
        var mayCollect = await permissions.CheckAsync(context, FinanceAction.CollectCustomerInstrument, instrumentId,
            cancellationToken) == PermissionDecision.Allowed;
        var again = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstruments, instrumentId,
            cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<CustomerInstrumentDetail>(ReadRefusal(again), null);
        }

        if (row is null)
        {
            return new ReadResult<CustomerInstrumentDetail>(ReadOutcome.NotFound, null);
        }

        var actions = CustomerInstrumentRules.StateActions((CustomerInstrumentKind)row.Kind,
                (CustomerInstrumentStatus)row.Status)
            .Where(candidate => candidate == CustomerInstrumentAction.Collect ? mayCollect : mayChange)
            .ToList();
        return new ReadResult<CustomerInstrumentDetail>(ReadOutcome.Found, new CustomerInstrumentDetail(
            View(row, Disclosure(amount)),
            [.. movements.Select(MovementView)],
            actions));
    }

    /// <summary>
    /// DEC-FIN-016 risk tracking (user decision 2026-09-27): the customer's debt closes at collection, and the
    /// instruments' risk is tracked separately — per customer account and currency (and their total), the count and
    /// face amount by risk state as of <see cref="CustomerInstrumentRiskRequest.AsOf"/>, and the open exposure. Read-only
    /// under <c>ReadCustomerInstruments</c>, fenced before and after the read like the list; every amount only with a
    /// fresh <c>ReadCustomerInstrumentAmount</c>. Facts only: no limit or warning (OPEN-010).
    /// </summary>
    public async Task<ReadResult<CustomerInstrumentRiskView>> RiskAuthorizedAsync(FinanceContext context,
        CustomerInstrumentRiskRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return new ReadResult<CustomerInstrumentRiskView>(ReadOutcome.Denied, null);
        }

        var read = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstruments, null, cancellationToken);
        if (read != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<CustomerInstrumentRiskView>(ReadRefusal(read), null);
        }

        // One customer account names both ids; a half-named scope reads nothing.
        if (request.PartyId is null != request.CompanyAccountId is null
            || request.PartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty)
        {
            return new ReadResult<CustomerInstrumentRiskView>(ReadOutcome.NotFound, null);
        }

        var query = dbContext.CustomerInstruments.AsNoTracking();
        if (request.PartyId is { } partyId && request.CompanyAccountId is { } accountId)
        {
            query = query.Where(row => row.PartyId == partyId && row.CompanyAccountId == accountId);
        }

        var facts = await query
            .Select(row => new CustomerInstrumentRiskFact(row.PartyId, row.CompanyAccountId, row.CurrencyId, row.Status,
                row.DueOn, row.AmountText))
            .ToListAsync(cancellationToken);
        var amount = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstrumentAmount, null, cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstruments, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<CustomerInstrumentRiskView>(ReadRefusal(again), null);
        }

        var (customers, totals) = CustomerInstrumentRiskPolicy.Build(facts, request.AsOf, Disclosure(amount));
        return new ReadResult<CustomerInstrumentRiskView>(ReadOutcome.Found, new CustomerInstrumentRiskView(
            context.CompanyId, request.AsOf, request.PartyId, request.CompanyAccountId, customers, totals));
    }

    /// <summary>The canonical content of a command: its name and the exact contract JSON of the request.</summary>
    internal static string PayloadHash<T>(string commandName, T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            commandName + "\n" + JsonSerializer.Serialize(request, Json)))).ToLowerInvariant();

    internal static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);

    /// <summary>Context, the action's first fence, and the key and context lengths — before anything is read.</summary>
    private async Task<CustomerInstrumentCommandResult?> StartAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, Guid? instrumentId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        if (await FenceAsync(context, action, instrumentId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var key = metadata.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, key.Trim(), StringComparison.Ordinal)
            || key.Length > IdentityMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));
        }

        return context.ActorId.Length > IdentityMaximumLength || context.CorrelationId.Length > IdentityMaximumLength
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("context", "finance_value_does_not_fit"))
            : null;
    }

    private async Task<(CustomerInstrumentCommandResult? Refusal, PartyAccountEligibility? Party)> PartyAsync(
        FinanceContext context, FinanceAction action, Guid? instrumentId, Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken)
    {
        var read = await parties.GetAuthorizedAsync(context, partyId, companyAccountId, cancellationToken);
        if (await FenceAsync(context, action, instrumentId, cancellationToken) is { } fenced)
        {
            return (fenced, null);
        }

        return read.Outcome switch
        {
            ReadOutcome.Denied => (Refused(context, CommandOutcome.Denied), null),
            ReadOutcome.NotFound => (Refused(context, CommandOutcome.ValidationFailed,
                Issue("companyAccountId", "party_account_ineligible")), null),
            ReadOutcome.Found when read.Resource is { } party =>
                party.CompanyId == context.CompanyId && party.PartyId == partyId
                && party.CompanyAccountId == companyAccountId && party.IsActive
                    ? (null, party)
                    : (Refused(context, CommandOutcome.ValidationFailed,
                        Issue("companyAccountId", "party_account_ineligible")), null),
            _ => (Refused(context, CommandOutcome.Unavailable, Issue("companyAccountId", "party_account_unavailable")), null),
        };
    }

    /// <summary>The Finance bank account for a deposit or a collection: approved, usable and in the instrument's currency.</summary>
    private async Task<(CustomerInstrumentCommandResult? Refusal, BankAccountView? Bank)> BankAsync(
        FinanceContext context, FinanceAction action, CustomerInstrumentRow row, Guid treasuryAccountId,
        CancellationToken cancellationToken)
    {
        var read = await banks.GetAuthorizedAsync(context, treasuryAccountId, cancellationToken);
        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } fenced)
        {
            return (fenced, null);
        }

        return read.Outcome switch
        {
            ReadOutcome.Denied => (Refused(context, CommandOutcome.Denied), null),
            ReadOutcome.NotFound => (Refused(context, CommandOutcome.ValidationFailed,
                Issue("treasuryAccountId", "bank_account_ineligible")), null),
            ReadOutcome.Found when read.Resource is { } bank =>
                bank.Id == treasuryAccountId && bank.CompanyId == context.CompanyId && bank.CurrencyId == row.CurrencyId
                && bank.IsActive && bank.IsUsableForReceipt
                    ? (null, bank)
                    : (Refused(context, CommandOutcome.ValidationFailed,
                        Issue("treasuryAccountId", "bank_account_ineligible")), null),
            _ => (Refused(context, CommandOutcome.Unavailable, Issue("treasuryAccountId", "bank_account_unavailable")), null),
        };
    }

    /// <summary>Settings' approved money profile for the currency on the date; its owner's codes kept as they are.</summary>
    private async Task<(CustomerInstrumentCommandResult? Refusal, MoneyProfileV1? Profile)> ProfileAsync(
        FinanceContext context, FinanceAction action, Guid? instrumentId, Guid currencyId, string operation,
        DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        MoneyProfileV1? profile;
        try
        {
            profile = await settings.GetMoneyProfileAsync(currencyId, operation, effectiveOn, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, action, instrumentId, cancellationToken) is { } fenced)
            {
                return (fenced, null);
            }

            return (exception.Code switch
            {
                "settings_access_denied" => Refused(context, CommandOutcome.Denied),
                // The currency has no approved profile: an answer about the request, not an outage.
                SettingsPrerequisiteCodesV1.MoneyProfileNotFound =>
                    Refused(context, CommandOutcome.ValidationFailed, Issue("money.currencyId", exception.Code)),
                _ => Refused(context, CommandOutcome.Unavailable, Issue("profile", exception.Code)),
            }, null);
        }

        return await FenceAsync(context, action, instrumentId, cancellationToken) is { } after
            ? (after, null)
            : (null, profile);
    }

    private async Task<CustomerInstrumentCommandResult?> PriorAsync(FinanceContext context, string commandName,
        FinanceAction action, string keyHash, string payloadHash, CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (prior.PayloadHash != payloadHash || !string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<CustomerInstrumentCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        var amount = await permissions.CheckAsync(context, FinanceAction.ReadCustomerInstrumentAmount,
            prior.CustomerInstrumentId, cancellationToken);
        if (await FenceAsync(context, action, prior.CustomerInstrumentId, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        return Disclose(stored with { IsReplay = true }, amount);
    }

    private async Task<CustomerInstrumentCommandResult?> SaveAsync(FinanceContext context, string commandName,
        FinanceAction action, string keyHash, string payloadHash, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, commandName, action, keyHash, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
    }

    private async Task<CustomerInstrumentCommandResult> TerminalAsync(FinanceContext context, string commandName,
        FinanceAction action, string keyHash, string payloadHash, Guid? instrumentId, CustomerInstrumentCommandResult refusal,
        CancellationToken cancellationToken)
    {
        AddCommand(context, commandName, keyHash, payloadHash, instrumentId, refusal, timeProvider.GetUtcNow());
        return await SaveAsync(context, commandName, action, keyHash, payloadHash, cancellationToken) ?? refusal;
    }

    private void AddCommand(FinanceContext context, string commandName, string keyHash, string payloadHash,
        Guid? instrumentId, CustomerInstrumentCommandResult result, DateTimeOffset now) =>
        dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            CustomerInstrumentId = instrumentId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    private async Task<CustomerInstrumentCommandResult?> FenceAsync(FinanceContext context, FinanceAction action,
        Guid? instrumentId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        var decision = await permissions.CheckAsync(context, action, instrumentId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("permission", "finance_permission_mapping_unavailable"));
        }

        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private static CustomerInstrumentMovementRow Movement(FinanceContext context, CustomerInstrumentRow row,
        CustomerInstrumentStatus? from, DateOnly occurredOn, Guid? treasuryAccountId, Guid? collectionSourceId,
        string? note, DateTimeOffset now) => new CustomerInstrumentMovementRow
    {
        Id = Guid.NewGuid(),
        CompanyId = context.CompanyId,
        InstrumentId = row.Id,
        Version = row.Version,
        FromStatus = from is null ? null : (int)from,
        ToStatus = row.Status,
        OccurredOn = occurredOn,
        TreasuryAccountId = treasuryAccountId,
        CollectionSourceId = collectionSourceId,
        Note = note,
        RecordedBy = context.ActorId,
        RecordedAtUtc = now,
        CorrelationId = context.CorrelationId,
    };

    private static string CollectionDescription(CustomerInstrumentRow row) =>
        (row.Kind == (int)CustomerInstrumentKind.Cheque ? "Müşteri çeki tahsili · " : "Müşteri senedi tahsili · ")
        + row.SerialNumber;

    private static CustomerInstrumentView View(CustomerInstrumentRow row, AmountDisclosure disclosure) => new CustomerInstrumentView(
        row.Id, row.CompanyId, VersionText(row.Version), (CustomerInstrumentKind)row.Kind,
        (CustomerInstrumentStatus)row.Status, row.PartyId, row.CompanyAccountId, row.SerialNumber, row.Drawer,
        row.BankName, row.BranchName, row.DueOn, row.ReceivedOn, row.CurrencyId,
        disclosure == AmountDisclosure.Visible
            ? new AmountView(AmountDisclosure.Visible, new MoneyText(row.AmountText, row.CurrencyId))
            : new AmountView(disclosure, null),
        row.Description, row.TreasuryAccountId, row.StatusChangedOn, row.CollectionSourceId, row.ReceivedBy,
        row.ReceivedAtUtc);

    private static CustomerInstrumentMovementView MovementView(CustomerInstrumentMovementRow row) => new CustomerInstrumentMovementView(
        VersionText(row.Version), row.FromStatus is { } from ? (CustomerInstrumentStatus)from : null,
        (CustomerInstrumentStatus)row.ToStatus, row.OccurredOn, row.TreasuryAccountId, row.CollectionSourceId, row.Note,
        row.RecordedBy, row.RecordedAtUtc);

    private static CustomerInstrumentCommandResult Succeeded(FinanceContext context, CustomerInstrumentRow row) =>
        new CustomerInstrumentCommandResult(
            CommandOutcome.Succeeded, View(row, AmountDisclosure.Visible), IsReplay: false, context.CorrelationId, []);

    /// <summary>A FRESH amount decision on a result (#497 P2-1): no amount of the resource survives without the grant.</summary>
    private static CustomerInstrumentCommandResult Disclose(CustomerInstrumentCommandResult result, PermissionDecision amount)
    {
        if (result.Resource is not { } resource || amount == PermissionDecision.Allowed)
        {
            return result;
        }

        return result with { Resource = resource with { Amount = new AmountView(Disclosure(amount), null) } };
    }

    private static AmountDisclosure Disclosure(PermissionDecision decision) => decision switch
    {
        PermissionDecision.Allowed => AmountDisclosure.Visible,
        PermissionDecision.Denied => AmountDisclosure.Redacted,
        _ => AmountDisclosure.Unavailable,
    };

    private static ReadOutcome ReadRefusal(PermissionDecision decision) =>
        decision == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied;

    private void Audit(FinanceContext context, CommandMetadata metadata, FinanceAuditEnvelope envelope, string entityType,
        Guid entityId, string eventType, string operation, long? versionBefore, long? versionAfter, string before,
        string after, string reason) =>
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId,
            eventType,
            entityType,
            entityId.ToString("D"),
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
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            context.CorrelationId,
            context.CausationId,
            metadata.IdempotencyKey));

    /// <summary>The instrument's identities, dates and status; the face value is never copied into audit JSON.</summary>
    private static string InstrumentAudit(CustomerInstrumentRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        version = VersionText(row.Version),
        kind = ((CustomerInstrumentKind)row.Kind).ToString(),
        status = ((CustomerInstrumentStatus)row.Status).ToString(),
        row.PartyId,
        row.CompanyAccountId,
        row.SerialNumber,
        row.BankName,
        row.DueOn,
        row.ReceivedOn,
        row.CurrencyId,
        row.TreasuryAccountId,
        row.StatusChangedOn,
        row.CollectionSourceId,
    }, AuditJson);

    /// <summary>The collection's receipt source, named with the instrument it came from; no amount.</summary>
    private static string CollectionSourceAudit(ReceiptDraftRow source, CustomerInstrumentRow instrument) =>
        JsonSerializer.Serialize(new
        {
            source.Id,
            version = VersionText(source.Version),
            stage = "Recorded",
            source.RecordedAtUtc,
            source.PartyId,
            source.CompanyAccountId,
            source.TreasuryAccountId,
            source.CurrencyId,
            source.OccurredOn,
            source.ValueOn,
            profile = new { source.ProfileId, source.ProfileVersion },
            channel = "CustomerInstrument",
            instrumentId = instrument.Id,
            instrumentKind = ((CustomerInstrumentKind)instrument.Kind).ToString(),
        }, AuditJson);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static CustomerInstrumentCommandResult Refused(FinanceContext context, CommandOutcome outcome,
        params ValidationIssue[] issues) =>
        new CustomerInstrumentCommandResult(outcome, null, IsReplay: false, context.CorrelationId, issues);
}
