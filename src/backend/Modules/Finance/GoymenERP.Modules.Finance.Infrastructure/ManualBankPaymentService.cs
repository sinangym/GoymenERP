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

public sealed record ManualBankPaymentBinding(string? MoneyOperation);

public sealed record ManualBankPaymentAuditEnvelopes(
    FinanceAuditEnvelope Draft,
    FinanceAuditEnvelope Approve,
    FinanceAuditEnvelope Record);

/// <summary>
/// R47 A-now manual outgoing bank payment. Draft and approval have no money effect. Only a successful,
/// explicitly confirmed final Record appends one outgoing event. Missing bank reference is allowed;
/// an unconfirmed result is not. The approver must differ from the preparer, while the separately
/// authorized final actor may be the approver or another authorized person.
/// </summary>
public sealed class ManualBankPaymentService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    IFinancePartyAccountPort parties,
    IManualBankPaymentDocumentValidationPort documents,
    ISettingsPrerequisiteProvider settings,
    IFinanceRecordPeriodRule periodRule,
    IAuditEventCollector auditEvents,
    ManualBankPaymentBinding binding,
    ManualBankPaymentAuditEnvelopes envelopes,
    TimeProvider timeProvider) : IManualBankPaymentService
{
    public const string DraftCommand = "finance.manual-bank-payment.draft.save";
    public const string ApproveCommand = "finance.manual-bank-payment.approve";
    public const string RecordCommand = "finance.manual-bank-payment.record";

    private const string EntityType = "Finance.ManualBankPayment";
    private const int ApprovedBankAccountStatus = 2;
    private const int IdentityMaximumLength = 128;
    private const int PageSize = 50;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<ManualBankPaymentCommandResult> DraftSaveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ManualBankPaymentDraftSaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (await FenceAsync(context, FinanceAction.SaveManualBankPaymentDraft, request.PaymentId, cancellationToken)
            is { } denied) return denied;
        if (!ValidMetadata(context, metadata))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(request);
        if (await PriorAsync(context, DraftCommand, FinanceAction.SaveManualBankPaymentDraft, keyHash, payloadHash,
                cancellationToken) is { } prior) return prior;
        if (!ManualBankPaymentValidator.IsValidDraftIdentity(request) || request.Preparation is null
            || request.Preparation.Money is null || request.Preparation.Profile is null)
            return Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "payment_identity_invalid"));
        if (!ManualBankPaymentValidator.IsValidLinkedDocument(request.LinkedDocument))
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("linkedDocument", "finance_manual_bank_payment_document_invalid"));

        ManualBankPaymentRow? row = null;
        if (request.PaymentId is { } paymentId)
        {
            row = await dbContext.ManualBankPayments.SingleOrDefaultAsync(candidate => candidate.Id == paymentId,
                cancellationToken);
            if (await FenceAsync(context, FinanceAction.SaveManualBankPaymentDraft, paymentId, cancellationToken)
                is { } afterRead) return afterRead;
            if (row is null)
                return await TerminalAsync(context, DraftCommand, FinanceAction.SaveManualBankPaymentDraft, keyHash,
                    payloadHash, null, Refused(context, CommandOutcome.ValidationFailed,
                        Issue("paymentId", "payment_not_found")), cancellationToken);
            if (VersionText(row.Version) != request.ExpectedVersion)
                return await TerminalAsync(context, DraftCommand, FinanceAction.SaveManualBankPaymentDraft, keyHash,
                    payloadHash, row.Id, Refused(context, CommandOutcome.VersionConflict,
                        Issue("expectedVersion", "version_conflict")), cancellationToken);
            if (row.Status != ManualBankPaymentStatuses.Draft)
                return Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "payment_not_draft"));
        }

        var preparation = request.Preparation;
        var bank = await ReadEligibleBankAsync(context, preparation, FinanceAction.SaveManualBankPaymentDraft,
            request.PaymentId, cancellationToken);
        if (bank.Result is { } bankFailure) return bankFailure;
        var party = await parties.GetAuthorizedAsync(context, preparation.PartyId, preparation.CompanyAccountId,
            cancellationToken);
        if (await FenceAsync(context, FinanceAction.SaveManualBankPaymentDraft, request.PaymentId, cancellationToken)
            is { } afterParty) return afterParty;
        if (party.Outcome != ReadOutcome.Found || party.Resource is null)
            return OwnerFailure(context, party.Outcome, "companyAccountId", "party_account_ineligible",
                "party_account_unavailable");

        if (await ValidatePreparationAsync(context, preparation, bank.View!, party.Resource, request.PaymentId,
                FinanceAction.SaveManualBankPaymentDraft, cancellationToken) is { } invalid) return invalid;

        var linked = await ResolveLinkedDocumentAsync(context, request.LinkedDocument, request.PaymentId,
            cancellationToken);
        if (linked.Result is { } linkedFailure) return linkedFailure;

        var now = timeProvider.GetUtcNow();
        var before = row is null ? "{}" : AuditJson(row);
        var versionBefore = row?.Version;
        if (row is null)
        {
            row = new ManualBankPaymentRow
            {
                Id = Guid.NewGuid(),
                CompanyId = context.CompanyId,
                Version = 1,
                Status = ManualBankPaymentStatuses.Draft,
                PreparedBy = context.ActorId,
                PreparedAtUtc = now,
            };
            Apply(row, preparation, linked.Reference);
            dbContext.ManualBankPayments.Add(row);
        }
        else
        {
            Apply(row, preparation, linked.Reference);
            row.Version = checked(row.Version + 1);
        }

        var result = Succeeded(context, row);
        AddCommand(context, DraftCommand, keyHash, payloadHash, row.Id, result, now);
        Audit(context, metadata, row, envelopes.Draft,
            versionBefore is null ? "finance.manual-bank-payment.created.v1" : "finance.manual-bank-payment.updated.v1",
            versionBefore is null ? "Create" : "Update", versionBefore, before);
        if (await FenceAsync(context, FinanceAction.SaveManualBankPaymentDraft, row.Id, cancellationToken)
            is { } beforeSave) return beforeSave;
        return await SaveAsync(context, DraftCommand, FinanceAction.SaveManualBankPaymentDraft, keyHash, payloadHash,
            cancellationToken) ?? result;
    }

    public async Task<ManualBankPaymentCommandResult> ApproveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ApproveManualBankPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (await FenceAsync(context, FinanceAction.ApproveManualBankPayment, request.PaymentId, cancellationToken)
            is { } denied) return denied;
        if (!ValidMetadata(context, metadata))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(request);
        if (await PriorAsync(context, ApproveCommand, FinanceAction.ApproveManualBankPayment, keyHash, payloadHash,
                cancellationToken) is { } prior) return prior;
        var row = await dbContext.ManualBankPayments.SingleOrDefaultAsync(candidate => candidate.Id == request.PaymentId,
            cancellationToken);
        if (await FenceAsync(context, FinanceAction.ApproveManualBankPayment, request.PaymentId, cancellationToken)
            is { } afterRead) return afterRead;
        if (row is null)
            return await TerminalAsync(context, ApproveCommand, FinanceAction.ApproveManualBankPayment, keyHash,
                payloadHash, null, Refused(context, CommandOutcome.ValidationFailed,
                    Issue("paymentId", "payment_not_found")), cancellationToken);
        if (VersionText(row.Version) != request.ExpectedVersion)
            return await TerminalAsync(context, ApproveCommand, FinanceAction.ApproveManualBankPayment, keyHash,
                payloadHash, row.Id, Refused(context, CommandOutcome.VersionConflict,
                    Issue("expectedVersion", "version_conflict")), cancellationToken);
        if (!ManualBankPaymentValidator.CanApprove(ToStatus(row.Status), row.PreparedBy, context.ActorId, request))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "payment_approval_invalid"));

        var before = AuditJson(row);
        var versionBefore = row.Version;
        var now = timeProvider.GetUtcNow();
        row.Status = ManualBankPaymentStatuses.Approved;
        row.ApprovedBy = context.ActorId;
        row.ApprovedAtUtc = now;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row);
        AddCommand(context, ApproveCommand, keyHash, payloadHash, row.Id, result, now);
        Audit(context, metadata, row, envelopes.Approve, "finance.manual-bank-payment.approved.v1", "Approve",
            versionBefore, before);
        if (await FenceAsync(context, FinanceAction.ApproveManualBankPayment, row.Id, cancellationToken)
            is { } beforeSave) return beforeSave;
        return await SaveAsync(context, ApproveCommand, FinanceAction.ApproveManualBankPayment, keyHash, payloadHash,
            cancellationToken) ?? result;
    }

    public async Task<ManualBankPaymentCommandResult> RecordAsync(
        FinanceContext context,
        CommandMetadata metadata,
        RecordManualBankPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (await FenceAsync(context, FinanceAction.RecordManualBankPayment, request.PaymentId, cancellationToken)
            is { } denied) return denied;
        if (!ValidMetadata(context, metadata))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(request);
        if (await PriorAsync(context, RecordCommand, FinanceAction.RecordManualBankPayment, keyHash, payloadHash,
                cancellationToken) is { } prior) return prior;
        var row = await dbContext.ManualBankPayments.SingleOrDefaultAsync(candidate => candidate.Id == request.PaymentId,
            cancellationToken);
        if (await FenceAsync(context, FinanceAction.RecordManualBankPayment, request.PaymentId, cancellationToken)
            is { } afterRead) return afterRead;
        if (row is null)
            return await TerminalAsync(context, RecordCommand, FinanceAction.RecordManualBankPayment, keyHash,
                payloadHash, null, Refused(context, CommandOutcome.ValidationFailed,
                    Issue("paymentId", "payment_not_found")), cancellationToken);
        if (VersionText(row.Version) != request.ExpectedVersion)
            return await TerminalAsync(context, RecordCommand, FinanceAction.RecordManualBankPayment, keyHash,
                payloadHash, row.Id, Refused(context, CommandOutcome.VersionConflict,
                    Issue("expectedVersion", "version_conflict")), cancellationToken);
        var recordIssues = ManualBankPaymentValidator.ValidateRecord(ToStatus(row.Status), request);
        if (recordIssues.Count > 0) return Refused(context, CommandOutcome.ValidationFailed, [.. recordIssues]);

        var preparation = Preparation(row);
        var bank = await ReadEligibleBankAsync(context, preparation, FinanceAction.RecordManualBankPayment, row.Id,
            cancellationToken);
        if (bank.Result is { } bankFailure) return bankFailure;
        var party = await parties.GetAuthorizedAsync(context, row.PartyId, row.CompanyAccountId, cancellationToken);
        if (await FenceAsync(context, FinanceAction.RecordManualBankPayment, row.Id, cancellationToken)
            is { } afterParty) return afterParty;
        if (party.Outcome != ReadOutcome.Found || party.Resource is null)
            return OwnerFailure(context, party.Outcome, "companyAccountId", "party_account_ineligible",
                "party_account_unavailable");
        if (await ValidatePreparationAsync(context, preparation, bank.View!, party.Resource, row.Id,
                FinanceAction.RecordManualBankPayment, cancellationToken) is { } invalid) return invalid;

        var now = timeProvider.GetUtcNow();
        var period = await periodRule.CheckAsync(context, row.OccurredOn, row.ValueOn, now, cancellationToken);
        if (await FenceAsync(context, FinanceAction.RecordManualBankPayment, row.Id, cancellationToken)
            is { } afterPeriod) return afterPeriod;
        if (period.Outcome != RecordPeriodOutcome.Open)
            return Refused(context, period.Outcome == RecordPeriodOutcome.Unavailable
                ? CommandOutcome.Unavailable : CommandOutcome.ValidationFailed,
                Issue("occurredOn", period.Code ?? (period.Outcome == RecordPeriodOutcome.Unavailable
                    ? RecordPeriodAnswer.RuleUnavailableCode : RecordPeriodAnswer.PeriodNotOpenCode)));

        var before = AuditJson(row);
        var versionBefore = row.Version;
        row.Status = ManualBankPaymentStatuses.Recorded;
        row.RecordedBy = context.ActorId;
        row.RecordedAtUtc = now;
        row.BankResultConfirmed = true;
        row.Version = checked(row.Version + 1);
        dbContext.OutgoingMoneyEvents.Add(new OutgoingMoneyEventRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            PaymentId = row.Id,
            PartyId = row.PartyId,
            CompanyAccountId = row.CompanyAccountId,
            TreasuryAccountId = row.TreasuryAccountId,
            CurrencyId = row.CurrencyId,
            AmountText = row.AmountText,
            ProfileId = row.ProfileId,
            ProfileVersion = row.ProfileVersion,
            OccurredOn = row.OccurredOn,
            ValueOn = row.ValueOn,
            RecordedAtUtc = now,
            RecordedBy = context.ActorId,
            CorrelationId = context.CorrelationId,
        });
        var result = Succeeded(context, row);
        AddCommand(context, RecordCommand, keyHash, payloadHash, row.Id, result, now);
        Audit(context, metadata, row, envelopes.Record, "finance.manual-bank-payment.recorded.v1", "Record",
            versionBefore, before);
        if (await FenceAsync(context, FinanceAction.RecordManualBankPayment, row.Id, cancellationToken)
            is { } beforeSave) return beforeSave;
        return await SaveAsync(context, RecordCommand, FinanceAction.RecordManualBankPayment, keyHash, payloadHash,
            cancellationToken) ?? result;
    }

    public async Task<ReadResult<ManualBankPaymentPage>> ListAuthorizedAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return new ReadResult<ManualBankPaymentPage>(ReadOutcome.Denied, null);
        var decision = await permissions.CheckAsync(context, FinanceAction.ReadManualBankPayments, null, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context)) return ReadFailure<ManualBankPaymentPage>(decision);
        var query = dbContext.ManualBankPayments.AsNoTracking();
        if (cursor is not null)
        {
            if (DraftListCursor.Decode(cursor, context.CompanyId) is not { } after)
                return new ReadResult<ManualBankPaymentPage>(ReadOutcome.NotFound, null);
            query = query.Where(row => row.PreparedAtUtc < after.CreatedAtUtc
                || (row.PreparedAtUtc == after.CreatedAtUtc && row.Id.CompareTo(after.Id) < 0));
        }
        var rows = await query.OrderByDescending(row => row.PreparedAtUtc).ThenByDescending(row => row.Id)
            .Take(PageSize + 1).ToListAsync(cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadManualBankPayments, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context)) return ReadFailure<ManualBankPaymentPage>(again);
        var page = rows.Take(PageSize).ToArray();
        var next = rows.Count > PageSize
            ? DraftListCursor.Encode(context.CompanyId, page[^1].PreparedAtUtc, page[^1].Id)
            : null;
        return new ReadResult<ManualBankPaymentPage>(
            ReadOutcome.Found,
            new ManualBankPaymentPage(context.CompanyId, page.Select(View).ToArray(), next));
    }

    public async Task<ReadResult<ManualBankPaymentView>> DetailAuthorizedAsync(
        FinanceContext context,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return new ReadResult<ManualBankPaymentView>(ReadOutcome.Denied, null);
        var decision = await permissions.CheckAsync(context, FinanceAction.ReadManualBankPayments, paymentId, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context)) return ReadFailure<ManualBankPaymentView>(decision);
        var row = await dbContext.ManualBankPayments.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == paymentId, cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadManualBankPayments, paymentId, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context)) return ReadFailure<ManualBankPaymentView>(again);
        return row is null
            ? new ReadResult<ManualBankPaymentView>(ReadOutcome.NotFound, null)
            : new ReadResult<ManualBankPaymentView>(ReadOutcome.Found, View(row));
    }

    internal static string Hash<T>(T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json))))
            .ToLowerInvariant();

    private async Task<(BankAccountView? View, ManualBankPaymentCommandResult? Result)> ReadEligibleBankAsync(
        FinanceContext context,
        ManualBankPaymentPreparation preparation,
        FinanceAction action,
        Guid? paymentId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == preparation.TreasuryAccountId, cancellationToken);
        if (await FenceAsync(context, action, paymentId, cancellationToken) is { } fenced) return (null, fenced);
        if (row is null || row.Status != ApprovedBankAccountStatus || row.MoneyMovementEligible != true
            || row.CurrencyId != preparation.Money.CurrencyId || string.IsNullOrWhiteSpace(row.Iban))
            return (null, Refused(context, CommandOutcome.ValidationFailed,
                Issue("treasuryAccountId", "bank_account_ineligible")));
        return (new BankAccountView(row.Id, row.CompanyId, row.CurrencyId, row.DisplayName,
            IsActive: true, IsUsableForReceipt: true, VersionText(row.Version)), null);
    }

    private async Task<ManualBankPaymentCommandResult?> ValidatePreparationAsync(
        FinanceContext context,
        ManualBankPaymentPreparation preparation,
        BankAccountView bank,
        PartyAccountEligibility party,
        Guid? paymentId,
        FinanceAction action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(binding.MoneyOperation))
            return Refused(context, CommandOutcome.Unavailable,
                Issue("profile", "finance_operation_mapping_unavailable"));
        try
        {
            var receiptShape = new ReceiptPreparation(preparation.PartyId, preparation.CompanyAccountId,
                preparation.TreasuryAccountId, preparation.Money, preparation.Profile, preparation.OccurredOn,
                preparation.ValueOn, preparation.BankReference, preparation.Description);
            var issues = await new SettingsReceiptPrerequisites(settings).ValidatePreparationAsync(context,
                binding.MoneyOperation, receiptShape, bank, party, cancellationToken);
            if (await FenceAsync(context, action, paymentId, cancellationToken) is { } fenced) return fenced;
            return issues.Count == 0 ? null : Refused(context, CommandOutcome.ValidationFailed, [.. issues]);
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, action, paymentId, cancellationToken) is { } fenced) return fenced;
            return exception.Code == "settings_access_denied"
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("profile", exception.Code));
        }
    }

    private async Task<(ManualBankPaymentLinkedDocument? Reference, ManualBankPaymentCommandResult? Result)>
        ResolveLinkedDocumentAsync(
            FinanceContext context,
            ManualBankPaymentLinkedDocumentRequest? requested,
            Guid? paymentId,
            CancellationToken cancellationToken)
    {
        if (requested is null) return (null, null);

        ManualBankPaymentDocumentValidationResult answer;
        try
        {
            answer = await documents.ValidateAsync(context, requested.DocumentId, requested.ExpectedVersion,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (await FenceAsync(context, FinanceAction.SaveManualBankPaymentDraft, paymentId, cancellationToken)
                is { } fenced) return (null, fenced);
            return (null, Refused(context, CommandOutcome.Unavailable,
                Issue("linkedDocument", "finance_manual_bank_payment_document_unavailable")));
        }

        if (await FenceAsync(context, FinanceAction.SaveManualBankPaymentDraft, paymentId, cancellationToken)
            is { } afterOwner) return (null, afterOwner);
        if (answer is null) return (null, Refused(context, CommandOutcome.Unavailable,
            Issue("linkedDocument", "finance_manual_bank_payment_document_unavailable")));

        if (answer.Outcome == ManualBankPaymentDocumentValidationOutcome.Found)
        {
            var reference = answer.Reference;
            if (reference is null || reference.DocumentId == Guid.Empty
                || reference.DocumentId != requested.DocumentId
                || !ManualBankPaymentValidator.IsOpaqueVersion(reference.Version)
                || !string.Equals(reference.Version, requested.ExpectedVersion, StringComparison.Ordinal))
                return (null, Refused(context, CommandOutcome.Unavailable,
                    Issue("linkedDocument", "finance_manual_bank_payment_document_unavailable")));
            return (reference, null);
        }

        return answer.Outcome switch
        {
            ManualBankPaymentDocumentValidationOutcome.Invalid => (null, Refused(context,
                CommandOutcome.ValidationFailed,
                Issue("linkedDocument", "finance_manual_bank_payment_document_invalid"))),
            ManualBankPaymentDocumentValidationOutcome.Denied => (null, Refused(context, CommandOutcome.Denied)),
            ManualBankPaymentDocumentValidationOutcome.NotFound => (null, Refused(context,
                CommandOutcome.ValidationFailed,
                Issue("linkedDocument", "finance_manual_bank_payment_document_not_found"))),
            ManualBankPaymentDocumentValidationOutcome.VersionConflict => (null, Refused(context,
                CommandOutcome.VersionConflict,
                Issue("linkedDocument", "finance_manual_bank_payment_document_version_conflict"))),
            _ => (null, Refused(context, CommandOutcome.Unavailable,
                Issue("linkedDocument", "finance_manual_bank_payment_document_unavailable"))),
        };
    }

    private async Task<ManualBankPaymentCommandResult?> PriorAsync(
        FinanceContext context,
        string command,
        FinanceAction action,
        string keyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (await FenceAsync(context, action, prior?.ManualBankPaymentId, cancellationToken) is { } fenced) return fenced;
        if (prior is null) return null;
        if (prior.PayloadHash != payloadHash || prior.ActorId != context.ActorId)
            return Refused(context, CommandOutcome.IdempotencyConflict);
        var stored = JsonSerializer.Deserialize<ManualBankPaymentCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayitli odeme komut sonucu okunamadi.");
        return stored with { IsReplay = true };
    }

    private async Task<ManualBankPaymentCommandResult?> SaveAsync(
        FinanceContext context,
        string command,
        FinanceAction action,
        string keyHash,
        string payloadHash,
        CancellationToken cancellationToken)
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
            return await PriorAsync(context, command, action, keyHash, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
    }

    private async Task<ManualBankPaymentCommandResult> TerminalAsync(
        FinanceContext context,
        string command,
        FinanceAction action,
        string keyHash,
        string payloadHash,
        Guid? paymentId,
        ManualBankPaymentCommandResult refusal,
        CancellationToken cancellationToken)
    {
        AddCommand(context, command, keyHash, payloadHash, paymentId, refusal, timeProvider.GetUtcNow());
        if (await FenceAsync(context, action, paymentId, cancellationToken) is { } fenced) return fenced;
        return await SaveAsync(context, command, action, keyHash, payloadHash, cancellationToken) ?? refusal;
    }

    private async Task<ManualBankPaymentCommandResult?> FenceAsync(
        FinanceContext context,
        FinanceAction action,
        Guid? paymentId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        var decision = await permissions.CheckAsync(context, action, paymentId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable,
                    Issue("permission", "finance_permission_mapping_unavailable"));
        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private void AddCommand(
        FinanceContext context,
        string command,
        string keyHash,
        string payloadHash,
        Guid? paymentId,
        ManualBankPaymentCommandResult result,
        DateTimeOffset now) => dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            ManualBankPaymentId = paymentId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    private void Audit(
        FinanceContext context,
        CommandMetadata metadata,
        ManualBankPaymentRow row,
        FinanceAuditEnvelope envelope,
        string eventType,
        string operation,
        long? versionBefore,
        string before) => auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId,
            eventType,
            EntityType,
            row.Id.ToString("D"),
            operation,
            versionBefore,
            row.Version,
            AuditActorTypes.User,
            context.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            AuditJson(row),
            "Manuel giden banka odemesi",
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            context.CorrelationId,
            context.CausationId,
            metadata.IdempotencyKey));

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && currentUser.UserId == context.ActorId && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);

    private static bool ValidMetadata(FinanceContext context, CommandMetadata metadata) =>
        !string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
        && metadata.IdempotencyKey == metadata.IdempotencyKey.Trim()
        && metadata.IdempotencyKey.Length <= IdentityMaximumLength
        && context.ActorId.Length <= IdentityMaximumLength
        && context.CorrelationId.Length <= IdentityMaximumLength;

    private static ManualBankPaymentStatus ToStatus(int status) => status switch
    {
        ManualBankPaymentStatuses.Draft => ManualBankPaymentStatus.Draft,
        ManualBankPaymentStatuses.Approved => ManualBankPaymentStatus.Approved,
        ManualBankPaymentStatuses.Recorded => ManualBankPaymentStatus.Recorded,
        _ => throw new InvalidOperationException("Bilinmeyen odeme durumu."),
    };

    private static void Apply(ManualBankPaymentRow row, ManualBankPaymentPreparation preparation,
        ManualBankPaymentLinkedDocument? linkedDocument)
    {
        row.PartyId = preparation.PartyId;
        row.CompanyAccountId = preparation.CompanyAccountId;
        row.TreasuryAccountId = preparation.TreasuryAccountId;
        row.CurrencyId = preparation.Money.CurrencyId;
        row.AmountText = preparation.Money.Amount;
        row.ProfileId = preparation.Profile.ProfileId;
        row.ProfileVersion = preparation.Profile.Version;
        row.OccurredOn = preparation.OccurredOn;
        row.ValueOn = preparation.ValueOn;
        row.BankReference = preparation.BankReference;
        row.Description = preparation.Description;
        row.LinkedDocumentId = linkedDocument?.DocumentId;
        row.LinkedDocumentVersion = linkedDocument?.Version;
    }

    private static ManualBankPaymentPreparation Preparation(ManualBankPaymentRow row) => new ManualBankPaymentPreparation(
        row.PartyId, row.CompanyAccountId, row.TreasuryAccountId,
        new MoneyText(row.AmountText, row.CurrencyId), new ProfileReference(row.ProfileId, row.ProfileVersion),
        row.OccurredOn, row.ValueOn, row.BankReference, row.Description);

    private static ManualBankPaymentView View(ManualBankPaymentRow row) => new ManualBankPaymentView(
        row.Id, row.CompanyId, ToStatus(row.Status), Preparation(row),
        row.LinkedDocumentId is { } documentId
            ? new ManualBankPaymentLinkedDocument(documentId, row.LinkedDocumentVersion!)
            : null,
        row.PreparedBy, row.PreparedAtUtc,
        row.ApprovedBy, row.ApprovedAtUtc, row.RecordedBy, row.RecordedAtUtc, row.BankResultConfirmed,
        VersionText(row.Version));

    private static ManualBankPaymentCommandResult Succeeded(FinanceContext context, ManualBankPaymentRow row) =>
        new ManualBankPaymentCommandResult(
            CommandOutcome.Succeeded, View(row), IsReplay: false, context.CorrelationId, []);

    private static ManualBankPaymentCommandResult Refused(
        FinanceContext context,
        CommandOutcome outcome,
        params ValidationIssue[] issues) => new ManualBankPaymentCommandResult(
            outcome, null, IsReplay: false, context.CorrelationId, issues);

    private static ReadResult<T> ReadFailure<T>(PermissionDecision decision) where T : class =>
        new ReadResult<T>(
            decision == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied,
            null);

    private static ManualBankPaymentCommandResult OwnerFailure(
        FinanceContext context,
        ReadOutcome outcome,
        string field,
        string notFoundCode,
        string unavailableCode) => outcome switch
        {
            ReadOutcome.Denied => Refused(context, CommandOutcome.Denied),
            ReadOutcome.NotFound => Refused(context, CommandOutcome.ValidationFailed, Issue(field, notFoundCode)),
            _ => Refused(context, CommandOutcome.Unavailable, Issue(field, unavailableCode)),
        };

    private static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);
    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static string AuditJson(ManualBankPaymentRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        version = VersionText(row.Version),
        status = ToStatus(row.Status).ToString(),
        row.PartyId,
        row.CompanyAccountId,
        row.TreasuryAccountId,
        row.CurrencyId,
        row.OccurredOn,
        row.ValueOn,
        profile = new { row.ProfileId, row.ProfileVersion },
        row.PreparedBy,
        row.ApprovedBy,
        row.RecordedBy,
        row.BankResultConfirmed,
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

public sealed class UnavailableManualBankPaymentDocumentValidationPort
    : IManualBankPaymentDocumentValidationPort
{
    public Task<ManualBankPaymentDocumentValidationResult> ValidateAsync(
        FinanceContext context,
        Guid documentId,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ManualBankPaymentDocumentValidationResult(
            ManualBankPaymentDocumentValidationOutcome.Unavailable, null));
    }
}
