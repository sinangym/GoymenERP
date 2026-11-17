using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>Immutable source queue and human proposal versions, using Accounting's existing journal/reversal owners.</summary>
public sealed partial class AccountingInventoryCostStoreV1(AccountingDbContext db, AccountingStore posting,
    IAccountingInventoryCostSourceReaderV1 sources, IAccountingCogsAccountResolverV1 cogs,
    IActiveAccountPlanReaderV1 plans, ISettingsMoneyCandidateReader currencies, AccountingAmountScaleVerifier amounts,
    ICompanyContext company, IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, TimeProvider clock,
    IPostedManufacturingShipmentCostReaderV1? postedManufacturing = null)
    : IAccountingInventoryCostStoreV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task RecordAttemptAsync(AccountingCommandRecord record, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default) => Atomic(new(record.ActorId, "", [], record.OriginalCorrelationId,
            null, record.IdempotencyKeyHash, SourceChannels.System), freshFence, () =>
        {
            db.Commands.Add(new() { CompanyId = Company(), CommandName = record.CommandName, IdempotencyKeyHash = record.IdempotencyKeyHash,
                ActorId = record.ActorId, PayloadHash = record.RequestHash, ResultJson = record.ResultJson,
                CorrelationId = record.OriginalCorrelationId, RecordedAtUtc = clock.GetUtcNow() });
            return Task.FromResult(true);
        }, cancellationToken);

    public async Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default)
    {
        var captured = Company();
        AccountingInventoryCostSourceValidationV1.RequireEnvelope(envelope, captured);
        var actor = "inbox." + AccountingInventoryCostInboxV1.ConsumerName;
        var metadata = new AccountingCommandMetadata(actor, actor, [], envelope.CorrelationId,
            envelope.CausationId ?? envelope.EventId.ToString("D"), envelope.EventId.ToString("D"), SourceChannels.System);
        await Atomic(metadata, null, async () =>
        {
            var previous = await db.InventoryCostSources.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == envelope.EventId, cancellationToken);
            if (previous is not null)
            {
                if (previous.PayloadHash != AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson)) throw Refuse("inventory_cost_source_conflict");
                return true; // Replay uses the accepted immutable proof, never today's Inventory/Logistics values.
            }
            var source = await sources.ReadAsync(envelope, cancellationToken);
            RequireCompany(captured);
            AccountingInventoryCostSourceValidationV1.Require(envelope, source, captured);
            if (await db.InventoryCostSources.AnyAsync(x => x.Kind == (int)source.Kind && x.InvoiceId == source.InvoiceId
                && x.InvoiceVersion == source.InvoiceVersion, cancellationToken)) throw Refuse("inventory_cost_source_conflict");
            db.InventoryCostSources.Add(new() { CompanyId = captured, EventId = source.EventId, Kind = (int)source.Kind,
                InvoiceId = source.InvoiceId, InvoiceVersion = source.InvoiceVersion, AdmissionId = source.AdmissionId,
                SourceJson = Encode(source), PayloadHash = source.SourcePayloadSha256,
                OccurredAtUtc = envelope.OccurredAtUtc, ReceivedAtUtc = clock.GetUtcNow() });
            audit.Enqueue(new AuditEventDraft(captured, "accounting.inventory-cost.source-received.v1", "Accounting.InventoryCostSource",
                source.EventId.ToString("D"), "received", null, 1, AuditActorTypes.System, actor, actor, [], "{}", Encode(source),
                "Gerçek kaynak kanıtı alındı; muhasebe fişi yazılmadı.", Encode(new { source.EventId, source.SourcePayloadSha256 }),
                AuditAiInvolvement.None, null, SourceChannels.System, null, null, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
            foreach (var component in source.Components)
            {
                await ClaimPhysicalCost(source, component, cancellationToken);
                AccountingInventoryCostAmountsV1.TryRead(component.UnpostedTlAmount ?? component.SignedTlAmount, out var amount, out _);
                var view = new AccountingInventoryCostProposalV1(Guid.NewGuid(), captured, source.EventId, source.Kind,
                    source.InvoiceId, source.InvoiceVersion, source.AdmissionId, source.InvoiceDate, source.OfficialInvoiceNumber,
                    component, 1, component.AlreadyPostedComponents is { Count: > 0 } && (component.UnpostedTlAmount is null || amount == 0) ? "AlreadyAccounted"
                        : amount == 0 && component.RecordedRounding is null ? "NoCost" : "AwaitingPreparation", envelope.OccurredAtUtc);
                Add(view);
                Audit(view, metadata, "received", null, system: true);
            }
            return true;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingInventoryCostProposalV1>> ListAsync(AccountingInventoryCostKindV1 kind,
        CancellationToken cancellationToken = default)
    {
        var captured = Company();
        if (!Enum.IsDefined(kind)) throw Refuse("inventory_cost_request_invalid");
        var rows = await (from row in db.InventoryCostProposals.AsNoTracking()
            join source in db.InventoryCostSources.AsNoTracking() on row.EventId equals source.EventId
            where source.Kind == (int)kind select row).ToListAsync(cancellationToken);
        var views = new List<AccountingInventoryCostProposalV1>();
        foreach (var row in rows.GroupBy(x => x.Id).Select(x => x.MaxBy(y => y.Version)!))
            views.Add(await CurrentState(await ReadStored(row, cancellationToken), cancellationToken));
        RequireCompany(captured);
        return views.OrderBy(x => x.SourceOccurredAtUtc).ThenBy(x => x.InvoiceId).ThenBy(x => x.Component.ComponentKey, StringComparer.Ordinal).ToArray();
    }

    public async Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken cancellationToken = default)
    {
        var captured = Company();
        var plan = await Plan(cancellationToken);
        var periods = await db.Periods.AsNoTracking().Where(x => x.Status == 1).OrderBy(x => x.StartsOn)
            .Select(x => new InvoiceLineCogsPeriodOptionV1(x.Id, x.StartsOn, x.EndsOn)).ToListAsync(cancellationToken);
        RequireCompany(captured);
        return new(captured, plan.Package.Mirror.Rows.OrderBy(x => x.Code, StringComparer.Ordinal)
            .Select(x => new InvoiceLineCogsAccountV1(x.Account.AccountId, x.Code, x.Name)).ToArray(), periods);
    }

    public Task<AccountingInventoryCostProposalV1> PrepareAsync(Guid proposalId, AccountingInventoryCostKindV1 kind,
        PrepareAccountingInventoryCostV1 request, Guid actorId, Func<CancellationToken, Task> freshFence,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        Atomic(metadata, freshFence, async () =>
        {
            Actor(actorId, metadata);
            var view = await Load(proposalId, kind, request.ExpectedVersion, cancellationToken);
            if (view.Status is not ("AwaitingPreparation" or "Prepared")) throw Refuse("inventory_cost_not_preparable");
            if (request.TransactionDate == default || string.IsNullOrWhiteSpace(request.Reason) || request.Reason != request.Reason.Trim()
                || request.Reason.Length > 1000) throw Refuse("inventory_cost_request_invalid");
            await Period(request.PeriodId, request.AccountingDate, cancellationToken);
            var classification = await Classification(view, request.AccountingDate, cancellationToken);
            var plan = await Plan(cancellationToken);
            var counter = plan.Package.Mirror.Lookup(new(Company(), request.CounterAccountId));
            if (counter is null || plan.Package.Mirror.Lookup(new(Company(), classification.AccountId)) is null
                || request.CounterAccountId == classification.AccountId) throw Refuse("inventory_cost_account_invalid");
            var currency = await TlCurrency(cancellationToken);
            var moneyProfile = await amounts.ReadProfileAsync(Company(), currency, request.AccountingDate, cancellationToken);
            RequireCompany(view.CompanyId);
            if (!AccountingInventoryCostRoundingV1Rules.TryResolve(view.Component, currency, moneyProfile.Profile.ProfileId,
                    moneyProfile.Profile.Version, moneyProfile.Scale, out var amount, out var rounding))
                throw Refuse(view.Component.RecordedRounding is null ? "inventory_cost_amount_not_representable" : "inventory_cost_source_money_profile_changed");
            var preparation = new AccountingInventoryCostPreparationV1(request.PeriodId, request.AccountingDate, request.TransactionDate,
                currency, amount.ToString("0.############################", CultureInfo.InvariantCulture),
                moneyProfile.Profile.ProfileId, moneyProfile.Profile.Version, moneyProfile.Scale, classification,
                new(counter.Account.AccountId, counter.Code, counter.Name), plan.PackageId, plan.Version, plan.CatalogVersion,
                plan.AccountPlanSourceVersion, plan.AccountsSourceSha256, actorId, clock.GetUtcNow(), request.Reason, rounding);
            var result = view with { Version = checked(view.Version + 1), Status = "Prepared", Preparation = preparation };
            Add(result); Audit(result, metadata, "prepared", view);
            return result;
        }, cancellationToken);

    public Task<AccountingInventoryCostProposalV1> ApproveAsync(Guid proposalId, AccountingInventoryCostKindV1 kind,
        long expectedVersion, Guid actorId, Func<CancellationToken, Task> freshFence,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        Atomic(metadata, freshFence, async () =>
        {
            Actor(actorId, metadata);
            var view = await Load(proposalId, kind, expectedVersion, cancellationToken);
            var prepared = view.Preparation;
            if (view.Status != "Prepared" || prepared is null || prepared.PreparedBy == actorId) throw Refuse("inventory_cost_approval_refused");
            await Period(prepared.PeriodId, prepared.AccountingDate, cancellationToken);
            var classification = await Classification(view, prepared.AccountingDate, cancellationToken);
            var plan = await Plan(cancellationToken);
            var moneyProfile = await amounts.ReadProfileAsync(Company(), prepared.CurrencyId, prepared.AccountingDate, cancellationToken);
            if (classification != prepared.Classification || plan.PackageId != prepared.AccountPlanPackageId
                || plan.Version != prepared.AccountPlanPackageVersion || plan.CatalogVersion != prepared.AccountPlanCatalogVersion
                || plan.AccountPlanSourceVersion != prepared.AccountPlanSourceVersion || plan.AccountsSourceSha256 != prepared.AccountPlanSha256
                || await TlCurrency(cancellationToken) != prepared.CurrencyId || moneyProfile.Profile.ProfileId != prepared.MoneyProfileId
                || moneyProfile.Profile.Version != prepared.MoneyProfileVersion || moneyProfile.Scale != prepared.MoneyScale)
                throw Refuse("inventory_cost_preparation_stale");
            decimal signed;
            if (prepared.Rounding is { } recordedRounding)
            {
                // Verification of the frozen preparation, not a second monetary rounding or source mutation.
                if (!AccountingInventoryCostRoundingV1Rules.TryResolve(view.Component, prepared.CurrencyId,
                        moneyProfile.Profile.ProfileId, moneyProfile.Profile.Version, moneyProfile.Scale,
                        out signed, out var expectedRounding) || recordedRounding != expectedRounding
                    || recordedRounding.RoundedSignedAmount != prepared.SignedAmount)
                    throw Refuse("inventory_cost_source_conflict");
            }
            else if (view.Component.UnpostedTlAmount is not null
                || !AccountingInventoryCostAmountsV1.TryExactDecimal(view.Component.SignedTlAmount, 28, out signed)
                || signed.ToString("0.############################", CultureInfo.InvariantCulture) != prepared.SignedAmount)
                throw Refuse("inventory_cost_source_conflict"); // An old exact preparation retains its original semantics.
            await freshFence(cancellationToken);
            if (signed == 0)
            {
                if (prepared.Rounding is null) throw Refuse("inventory_cost_source_conflict");
                var zeroResult = view with { Version = checked(view.Version + 1), Status = "RoundedNoCost", ApprovedBy = actorId,
                    ApprovedAtUtc = clock.GetUtcNow() };
                Add(zeroResult); Audit(zeroResult, metadata, "approved-zero-without-journal", view);
                return zeroResult;
            }
            var purpose = view.Kind == AccountingInventoryCostKindV1.SalesInvoicedShipment
                ? AccountingInventoryCostTraceV1.SalesPurpose(view.Component.InvoiceLineId!.Value, view.InvoiceVersion)
                : AccountingInventoryCostTraceV1.PurchaseDifferencePurpose;
            var sourceId = view.Kind == AccountingInventoryCostKindV1.SalesInvoicedShipment ? view.InvoiceId : view.ProposalId;
            var cogsSide = signed > 0 ? PostingSide.Debit : PostingSide.Credit;
            var counterSide = signed > 0 ? PostingSide.Credit : PostingSide.Debit;
            var request = new PostingRequest(AccountingInventoryCostInboxV1.ConsumerName, view.ProposalId.ToString("D"),
                Guid.NewGuid(), new(Company(), sourceId, purpose), "human-cost-proposal/" + view.Version.ToString(CultureInfo.InvariantCulture),
                prepared.CurrencyId, new(prepared.TransactionDate, prepared.AccountingDate, null, clock.GetUtcNow()),
                [new("cogs", new(Company(), classification.AccountId), cogsSide, Math.Abs(signed)),
                 new("selected-counter", new(Company(), prepared.CounterAccount.AccountId), counterSide, Math.Abs(signed))]);
            var posted = await posting.ReceiveAsync(request, prepared.PeriodId,
                metadata with { Completion = null, FreshAuthorityFence = freshFence }, cancellationToken);
            if (posted.Kind != PostingOutcomeKind.Posted || posted.Journal is null) throw Refuse("inventory_cost_posting_conflict");
            var result = view with { Version = checked(view.Version + 1), Status = "Posted", ApprovedBy = actorId,
                ApprovedAtUtc = clock.GetUtcNow(), JournalId = posted.Journal.Id };
            Add(result); Audit(result, metadata, "approved", view);
            return result;
        }, cancellationToken);

    private async Task<AccountingInventoryCostProposalV1> Load(Guid id, AccountingInventoryCostKindV1 kind, long version, CancellationToken token)
    {
        // First resolve immutable identity, then use the same cancellation -> proposal order as the revenue voucher owner.
        var identity = await db.InventoryCostProposals.AsNoTracking().Where(x => x.Id == id).OrderBy(x => x.Version)
            .FirstOrDefaultAsync(token) ?? throw Refuse("inventory_cost_not_found");
        var first = await ReadStored(identity, token);
        if (first.Kind != kind) throw Refuse("inventory_cost_not_found");
        var cancellation = await Cancellation(first, token);
        var row = await db.InventoryCostProposals.AsNoTracking().Where(x => x.Id == id).OrderByDescending(x => x.Version).FirstAsync(token);
        if (version < 1 || row.Version != version) throw new AccountingVersionConflictException(version, row.Version);
        if (cancellation is not null) throw Refuse("inventory_cost_source_cancelled");
        return await ReadStored(row, token);
    }

    private void Add(AccountingInventoryCostProposalV1 view) => db.InventoryCostProposals.Add(new()
    {
        CompanyId = view.CompanyId, Id = view.ProposalId, Version = view.Version, EventId = view.EventId,
        ComponentKey = view.Component.ComponentKey, Status = view.Status switch
        { "AwaitingPreparation" => 1, "Prepared" => 2, "Posted" => 3, "NoCost" => 4, "AlreadyAccounted" => 5, "RoundedNoCost" => 6, _ => throw Refuse("inventory_cost_state_invalid") },
        PreparedBy = view.Preparation?.PreparedBy, ApprovedBy = view.ApprovedBy, PeriodId = view.Preparation?.PeriodId,
        JournalId = view.JournalId, ViewJson = Encode(view), RecordedAtUtc = clock.GetUtcNow(),
    });

    private async Task<T> Atomic<T>(AccountingCommandMetadata metadata, Func<CancellationToken, Task>? fence,
        Func<Task<T>> operation, CancellationToken token)
    {
        var captured = Company();
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || db.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw Refuse("inventory_cost_source_unavailable");
        var started = false;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            started = true;
            if (fence is not null) await fence(token);
            var result = await operation();
            if (fence is not null) await fence(token);
            RequireCompany(captured);
            if (metadata.Completion?.Invoke(result!) is { } command) db.Commands.Add(new()
            { CompanyId = captured, CommandName = command.CommandName, IdempotencyKeyHash = command.IdempotencyKeyHash,
                ActorId = command.ActorId, PayloadHash = command.RequestHash, ResultJson = command.ResultJson,
                CorrelationId = command.OriginalCorrelationId, RecordedAtUtc = clock.GetUtcNow() });
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), token);
            await db.SaveChangesAsync(token);
            if (fence is not null) await fence(token);
            RequireCompany(captured);
            await transaction.CommitAsync(token);
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw Refuse(AccountingStoreCodes.ConcurrentChange); }
        catch (DbUpdateException e) when (e.GetBaseException() is SqlException { Number: 2601 or 2627 or 1205 }) { throw Refuse(AccountingStoreCodes.ConcurrentChange); }
        catch (SqlException e) when (e.Number == 1205) { throw Refuse(AccountingStoreCodes.ConcurrentChange); }
        catch (DbUpdateException) { throw Refuse("inventory_cost_source_unavailable"); }
        catch (System.Data.Common.DbException) { throw Refuse("inventory_cost_source_unavailable"); }
        finally { if (started) { db.ChangeTracker.Clear(); auditWriter.DiscardPending(); } }
    }

    private void Audit(AccountingInventoryCostProposalV1 view, AccountingCommandMetadata metadata, string action,
        AccountingInventoryCostProposalV1? before, bool system = false) => audit.Enqueue(new AuditEventDraft(view.CompanyId,
        "accounting.inventory-cost." + action + ".v1", "Accounting.InventoryCostProposal", view.ProposalId.ToString("D"), action,
        before?.Version, view.Version, system ? AuditActorTypes.System : AuditActorTypes.User,
        metadata.ActorId, metadata.PermissionCode, metadata.RoleCodes, before is null ? "{}" : Encode(before), Encode(view),
        view.Preparation?.Reason ?? "Gerçek stok maliyeti muhasebe önerisi olarak alındı; yevmiye kaydı oluşturulmadı.",
        Encode(new { view.EventId, view.InvoiceId, view.Component.SourceProofSha256 }), AuditAiInvolvement.None, null,
        metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
    private Guid Company() => company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId : throw Refuse(AccountingStoreCodes.CompanyContextMismatch);
    private void RequireCompany(Guid id) { if (Company() != id) throw Refuse(AccountingStoreCodes.CompanyContextMismatch); }
    private static void Actor(Guid id, AccountingCommandMetadata metadata)
    { if (id == Guid.Empty || !Guid.TryParse(metadata.ActorId, out var actor) || actor != id) throw Refuse("inventory_cost_authority_denied"); }
    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Json) ?? throw Refuse("inventory_cost_source_unavailable");
    private static AccountingStoreException Refuse(string code) => new(code, "SMM kaynak önerisi işlemi tamamlanamadı.");
}
