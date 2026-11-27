using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>Own-table inbox and human proposal; only the existing Accounting posting store writes a journal.</summary>
public sealed partial class ManufacturingCostDifferenceStoreV1(AccountingDbContext db,
    AccountingStore posting, ICompanyContext company, IActiveAccountPlanReaderV1 accountPlans,
    ISettingsMoneyCandidateReader currencies, IAuditEventCollector audit, TimeProvider clock,
    ManufacturingPostedCostAttributionStoreV1? attributions = null)
    : IManufacturingCostDifferenceStoreV1
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private const string EntityType = "Accounting.ManufacturingCostDifference";

    public async Task AcceptAsync(WorkInboxEventV1 envelope, ManufacturingCostDifferenceEventV1 source,
        CancellationToken cancellationToken = default)
    {
        // The store repeats the public consumer's validation; a direct caller cannot bypass source identity.
        var decoded = ManufacturingCostDifferenceInboxConsumerV1.Read(envelope, company);
        if (JsonSerializer.Serialize(decoded, Json) != JsonSerializer.Serialize(source, Json)) throw Refuse("cost_difference_source_conflict");
        var companyId = Company();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.PayloadJson)));
        var previous = await db.ManufacturingCostDifferences.AsNoTracking().SingleOrDefaultAsync(x => x.Id == envelope.EventId, cancellationToken);
        RequireCompany(companyId);
        if (previous is not null)
        {
            if (previous.SourceHash != hash) throw Refuse("cost_difference_source_conflict");
            return;
        }
        if (!ManufacturingCostDifferenceServiceV1.TryCostDifferenceSignedTotal(source, out var amount, out var signedTotal))
            throw Refuse("cost_difference_amount_unreadable");
        var row = new ManufacturingCostDifferenceRow
        {
            Id = envelope.EventId, CompanyId = companyId, ManufacturingOrderId = source.ManufacturingOrderId,
            GenerationId = source.GenerationId, GenerationVersion = source.GenerationVersion, SourceHash = hash,
            SourceJson = envelope.PayloadJson, SourceOccurredAtUtc = envelope.OccurredAtUtc,
            SourceCorrelationId = envelope.CorrelationId, SignedTlDifference = signedTotal, Status = amount == 0 ? 4 : 1, Version = 1,
        };
        db.ManufacturingCostDifferences.Add(row);
        var actor = "inbox." + ManufacturingCostDifferenceInboxV1.ConsumerName;
        var metadata = new AccountingCommandMetadata(actor, actor, [], envelope.CorrelationId,
            envelope.CausationId ?? envelope.EventId.ToString("D"), envelope.EventId.ToString("D"), SourceChannels.System);
        Audit(row, metadata, "received", null, "{}", system: true);
        RequireCompany(companyId);
        await Save(cancellationToken);
    }

    public async Task<IReadOnlyList<ManufacturingCostDifferenceViewV1>> ListAsync(CancellationToken cancellationToken = default)
    {
        var companyId = Company();
        var rows = await db.ManufacturingCostDifferences.AsNoTracking().OrderByDescending(x => x.SourceOccurredAtUtc)
            .ThenBy(x => x.Id).ToListAsync(cancellationToken);
        RequireCompany(companyId);
        return rows.Select(View).ToArray();
    }

    public async Task<ManufacturingCostDifferenceViewV1?> FindAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        var companyId = Company();
        var row = await db.ManufacturingCostDifferences.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposalId, cancellationToken);
        RequireCompany(companyId);
        return row is null ? null : View(row);
    }

    public async Task<ManufacturingCostDifferenceViewV1> PrepareAsync(PrepareManufacturingCostDifferenceV1 request,
        Guid actorId, Func<CancellationToken, Task> freshAuthorityFence, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var companyId = Company();
        await freshAuthorityFence(cancellationToken);
        RequireActor(actorId, metadata);
        var row = await Load(request.ProposalId, request.ExpectedVersion, cancellationToken);
        if (row.Status is not (1 or 2)) throw Refuse("cost_difference_not_preparable");
        if (actorId == Guid.Empty || request.DebitAccountId == Guid.Empty || request.CreditAccountId == Guid.Empty
            || request.DebitAccountId == request.CreditAccountId || request.AccountingDate == default
            || request.TransactionDate == default || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason != request.Reason.Trim() || request.Reason.Length > 1000) throw Refuse("cost_difference_preparation_invalid");
        await RequireOpenPeriod(request.PeriodId, request.AccountingDate, cancellationToken);
        var plan = await ActivePlan(companyId, request.DebitAccountId, request.CreditAccountId, cancellationToken);
        var currency = await TlCurrency(cancellationToken);
        await freshAuthorityFence(cancellationToken);
        RequireCompany(companyId);
        var before = JsonSerializer.Serialize(View(row), Json);
        var version = row.Version;
        row.Status = 2;
        row.PeriodId = request.PeriodId;
        row.AccountingDate = request.AccountingDate;
        row.TransactionDate = request.TransactionDate;
        row.CurrencyId = currency;
        row.DebitAccountId = request.DebitAccountId;
        row.CreditAccountId = request.CreditAccountId;
        row.AccountPlanVersion = plan.AccountPlanSourceVersion;
        row.AccountPlanSha256 = plan.AccountsSourceSha256;
        var debit = plan.Package.Mirror.Lookup(new AccountRef(companyId, request.DebitAccountId))!;
        var credit = plan.Package.Mirror.Lookup(new AccountRef(companyId, request.CreditAccountId))!;
        row.AccountPlanProofJson = JsonSerializer.Serialize(new ManufacturingCostDifferenceAccountPlanV1(plan.PackageId,
            plan.Version, plan.CatalogVersion, plan.AccountPlanSourceVersion, plan.AccountsSourceSha256,
            new ManufacturingCostDifferenceAccountV1(debit.Account.AccountId, debit.Code, debit.Name), new ManufacturingCostDifferenceAccountV1(credit.Account.AccountId, credit.Code, credit.Name)), Json);
        row.PreparedBy = actorId;
        row.PreparedAtUtc = clock.GetUtcNow();
        row.Reason = request.Reason;
        row.Version = checked(version + 1);
        var result = View(row);
        Audit(row, metadata, "prepared", version, before);
        Complete(metadata, companyId, result);
        await Save(cancellationToken);
        return result;
    }

    public async Task<ManufacturingCostDifferenceViewV1> ApproveAsync(Guid proposalId, long expectedVersion, Guid actorId,
        Func<CancellationToken, Task> freshAuthorityFence, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var companyId = Company();
        await freshAuthorityFence(cancellationToken);
        RequireActor(actorId, metadata);
        var owned = IsOwnedApproval;
        await using var transaction = owned ? null : await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var row = await Load(proposalId, expectedVersion, cancellationToken, owned ? freshAuthorityFence : null);
            if (row.Status != 2 || row.PreparedBy is null || actorId == Guid.Empty || row.PreparedBy == actorId)
                throw Refuse("cost_difference_approval_refused");
            await RequireOpenPeriod(row.PeriodId!.Value, row.AccountingDate!.Value, cancellationToken, owned ? freshAuthorityFence : null);
            var plan = await ActivePlan(companyId, row.DebitAccountId!.Value, row.CreditAccountId!.Value, cancellationToken, owned ? freshAuthorityFence : null);
            var preparedPlan = JsonSerializer.Deserialize<ManufacturingCostDifferenceAccountPlanV1>(row.AccountPlanProofJson!, Json);
            if (plan.AccountPlanSourceVersion != row.AccountPlanVersion || plan.AccountsSourceSha256 != row.AccountPlanSha256
                || preparedPlan is null || preparedPlan.PackageId != plan.PackageId || preparedPlan.PackageVersion != plan.Version
                || preparedPlan.CatalogVersion != plan.CatalogVersion
                || await TlCurrency(cancellationToken, owned ? freshAuthorityFence : null) != row.CurrencyId) throw Refuse("cost_difference_source_changed");
            await freshAuthorityFence(cancellationToken);
            RequireCompany(companyId);
            var before = JsonSerializer.Serialize(View(row), Json);
            var version = row.Version;
            var amount = Math.Abs(decimal.Parse(row.SignedTlDifference, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
            // Accounts and sides were explicitly selected and are now approved by another person. They are not a
            // fabricated reusable posting-rule snapshot. The source's sign stays visible in the immutable proposal.
            var request = new PostingRequest(ManufacturingCostDifferenceInboxV1.ConsumerName,
                row.Id.ToString("D"), Guid.NewGuid(), new SourceReference(companyId, row.Id, ManufacturingCostDifferenceInboxV1.PostingPurpose),
                "human-proposal/" + row.Version.ToString(CultureInfo.InvariantCulture), row.CurrencyId!.Value,
                new PostingDates(row.TransactionDate!.Value, row.AccountingDate.Value, null, clock.GetUtcNow()),
                [new ProposedLine("selected-debit", new AccountRef(companyId, row.DebitAccountId.Value), PostingSide.Debit, amount),
                 new ProposedLine("selected-credit", new AccountRef(companyId, row.CreditAccountId.Value), PostingSide.Credit, amount)]);
            var outcome = await posting.ReceiveAsync(request, row.PeriodId.Value, metadata with { Completion = null }, cancellationToken);
            if (outcome.Journal is null || outcome.Kind != PostingOutcomeKind.Posted) throw Refuse("cost_difference_posting_conflict");
            await freshAuthorityFence(cancellationToken);
            RequireCompany(companyId);
            row.Status = 3;
            row.ApprovedBy = actorId;
            row.ApprovedAtUtc = clock.GetUtcNow();
            row.JournalId = outcome.Journal.Id;
            row.Version = checked(version + 1);
            var result = View(row);
            Audit(row, metadata, "approved", version, before);
            Complete(metadata, companyId, result);
            await Save(cancellationToken);
            var frozenSource = JsonSerializer.Deserialize<ManufacturingCostDifferenceEventV1>(row.SourceJson, Json)
                ?? throw Refuse("manufacturing_attribution_source_missing");
            if (frozenSource.ConsumedLineageReference is not null)
            {
                if (attributions is null) throw Refuse("manufacturing_attribution_owner_unavailable");
                await attributions.RecordPostedAsync(row.Id, freshAuthorityFence, cancellationToken);
                if (owned) await freshAuthorityFence(cancellationToken);
                await Save(cancellationToken);
            }
            await freshAuthorityFence(cancellationToken);
            RequireCompany(companyId);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            if (owned) throw; // The whole-command owner alone aborts/disposes/cleans before a single original-key read.
            await transaction!.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            // SQL Server already selected this posting transaction as the deadlock victim. EF's SQL Server
            // execution strategy wraps that precise save failure; no other InvalidOperationException is a conflict.
            if (exception is InvalidOperationException
                { InnerException: DbUpdateException { InnerException: SqlException { Number: 1205 } } })
                throw Refuse(AccountingStoreCodes.ConcurrentChange, exception);
            throw;
        }
    }

    private async Task<ManufacturingCostDifferenceRow> Load(Guid id, long version, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? approvalFence = null)
    {
        var row = await db.ManufacturingCostDifferences.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw Refuse("cost_difference_not_found");
        if (approvalFence is not null) await approvalFence(cancellationToken);
        if (version < 1 || row.Version != version) throw new AccountingVersionConflictException(version, row.Version);
        return row;
    }

    private async Task RequireOpenPeriod(Guid periodId, DateOnly date, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? approvalFence = null)
    {
        var record = await posting.FindPeriodAsync(periodId, cancellationToken);
        if (approvalFence is not null) await approvalFence(cancellationToken);
        if (record is null || record.Period.Status != AccountingPeriodStatus.Open || date < record.Period.StartsOn
            || date > record.Period.EndsOn) throw Refuse("cost_difference_open_period_required");
    }

    private async Task<ApprovedAccountingSourceCatalogRecordV1> ActivePlan(Guid companyId, Guid debit, Guid credit, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? approvalFence = null)
    {
        var plan = await accountPlans.ReadActiveAsync(cancellationToken);
        if (approvalFence is not null) await approvalFence(cancellationToken);
        if (plan is null || plan.CompanyId != companyId || plan.Status != ApprovedAccountingSourceCatalogStatusV1.Active
            || plan.Package.Mirror.CompanyId != companyId) throw Refuse("cost_difference_account_plan_missing");
        if (plan.Package.Mirror.Lookup(new AccountRef(companyId, debit)) is null
            || plan.Package.Mirror.Lookup(new AccountRef(companyId, credit)) is null) throw Refuse("cost_difference_account_not_in_plan");
        return plan;
    }

    private async Task<Guid> TlCurrency(CancellationToken cancellationToken, Func<CancellationToken, Task>? approvalFence = null)
    {
        var current = await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken);
        if (approvalFence is not null) await approvalFence(cancellationToken);
        var candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(current);
        var matches = candidates.Where(x => x.Code == "TRY").ToArray();
        if (matches.Length != 1) throw Refuse("cost_difference_tl_currency_unavailable");
        return matches[0].CurrencyId;
    }

    private void Complete(AccountingCommandMetadata metadata, Guid companyId, ManufacturingCostDifferenceViewV1 view)
    {
        if (metadata.Completion?.Invoke(view) is not { } command) return;
        if (IsOwnedApproval) { posting.CompleteOwnedManufacturingCostDifferenceApproval(this, db, audit, command); return; }
        db.Commands.Add(new AccountingCommandRow { CompanyId = companyId, CommandName = command.CommandName,
            IdempotencyKeyHash = command.IdempotencyKeyHash, ActorId = command.ActorId, PayloadHash = command.RequestHash,
            ResultJson = command.ResultJson, CorrelationId = command.OriginalCorrelationId, RecordedAtUtc = clock.GetUtcNow() });
    }

    private void Audit(ManufacturingCostDifferenceRow row, AccountingCommandMetadata metadata, string action,
        long? beforeVersion, string before, bool system = false) => audit.Enqueue(new AuditEventDraft(row.CompanyId,
            "accounting.manufacturing-cost-difference." + action + ".v1", EntityType, row.Id.ToString("D"), action,
            beforeVersion, row.Version, system ? AuditActorTypes.System : AuditActorTypes.User, metadata.ActorId,
            metadata.PermissionCode, metadata.RoleCodes, before, JsonSerializer.Serialize(View(row), Json),
            row.Reason ?? "Üretim kaynağının tüketilen bölüm maliyet farkı ayrı muhasebe önerisidir.",
            JsonSerializer.Serialize(new { row.PreparedBy, row.ApprovedBy, sourceEventId = row.Id }, Json),
            AuditAiInvolvement.None, null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

    private static ManufacturingCostDifferenceViewV1 View(ManufacturingCostDifferenceRow row)
    {
        var source = JsonSerializer.Deserialize<ManufacturingCostDifferenceEventV1>(row.SourceJson, Json)
            ?? throw new InvalidOperationException("Maliyet farkı kaynak belgesi okunamadı.");
        return new ManufacturingCostDifferenceViewV1(row.Id, row.CompanyId, row.ManufacturingOrderId, row.GenerationId, row.GenerationVersion,
            row.Status switch { 1 => "AwaitingPreparation", 2 => "Prepared", 3 => "Posted", 4 => "NoDifference", _ => throw new InvalidOperationException() },
            row.Version, row.SignedTlDifference, ManufacturingCostDifferenceServiceV1.CostDifferenceOutputs(source), row.SourceOccurredAtUtc, row.PeriodId, row.AccountingDate,
            row.TransactionDate, row.DebitAccountId, row.CreditAccountId, row.PreparedBy, row.ApprovedBy, row.JournalId, row.Reason,
            row.AccountPlanProofJson is null ? null : JsonSerializer.Deserialize<ManufacturingCostDifferenceAccountPlanV1>(row.AccountPlanProofJson, Json));
    }
    private Guid Company() => company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId
        : throw Refuse(AccountingStoreCodes.CompanyContextMismatch);
    private void RequireCompany(Guid captured) { if (Company() != captured) throw Refuse(AccountingStoreCodes.CompanyContextMismatch); }
    private static void RequireActor(Guid actor, AccountingCommandMetadata metadata)
    {
        if (actor == Guid.Empty || !Guid.TryParse(metadata.ActorId, out var auditActor) || actor != auditActor)
            throw Refuse("cost_difference_approval_refused");
    }
    private static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static AccountingStoreException Refuse(string code, Exception? inner = null) =>
        new AccountingStoreException(code, "Üretim maliyet farkı işlemi tamamlanamadı.", inner);

    private async Task Save(CancellationToken cancellationToken)
    {
        try
        {
            if (IsOwnedApproval) await posting.SaveOwnedManufacturingCostDifferenceApprovalAsync(this, db, audit, cancellationToken);
            else await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!IsOwnedApproval) db.ChangeTracker.Clear();
            throw Refuse(AccountingStoreCodes.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (!IsOwnedApproval) db.ChangeTracker.Clear();
            throw Refuse(AccountingStoreCodes.ConcurrentChange);
        }
    }
}
