using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>B-28: accepts the existing fact once; no accounting amount, mapping or journal is invented.</summary>
public sealed class FinanceJournalSourceStoreV1(AccountingDbContext db, ICompanyContext company,
    IApprovedNormalJournalSourceKindCatalogV1 kinds, IApprovedNormalJournalSourceBindingCatalogV1 bindings,
    IAuditEventCollector audit, TimeProvider clock, IAuditTransactionWriterV1? auditOwner = null,
    IFinanceCashExchangeJournalSourceProviderV1? exchanges = null) : IFinanceJournalSourceStoreV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken)
    {
        if (db.ChangeTracker.HasChanges() || auditOwner is { HasPendingDrafts: true })
            throw new InvalidOperationException("Finans kaynak kabulü başka bir bekleyen yazıyla birleştirilemez.");
        var source = FinanceJournalSourceReaderV1.Read(envelope, company);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.PayloadJson)));
        var matches = await db.FinanceJournalSources.AsNoTracking().Where(x => x.Id == envelope.EventId
            || x.SourceId == source.Id && x.SourceKind == source.Kind && x.SourceVersion == source.Version).Take(2).ToListAsync(cancellationToken);
        Fence(source.CompanyId);
        if (matches.Count > 1)
            throw new OutboxPermanentFailureException("finance_journal_source_conflict", "Finans olay kimliği ile kaynak kimliği farklı kayıtları işaret ediyor.");
        var prior = matches.SingleOrDefault();
        if (prior is not null)
        {
            if (prior.Id != envelope.EventId || prior.SourceHash != hash)
                throw new OutboxPermanentFailureException("finance_journal_source_conflict", "Finans kaynağı aynı kimlik veya sürümle farklı içerik taşıyor.");
            return;
        }
        var row = new FinanceJournalSourceRow
        {
            Id = envelope.EventId, CompanyId = source.CompanyId, SourceId = source.Id, SourceKind = source.Kind,
            SourceVersion = source.Version, CurrencyId = source.CurrencyId, AmountText = source.Amount,
            OwnerProfileId = source.ProfileId, OwnerProfileVersion = source.ProfileVersion, OccurredOn = source.OccurredOn,
            RecordedAtUtc = source.RecordedAtUtc, ReceivedAtUtc = clock.GetUtcNow(), SourceHash = hash, SourceJson = envelope.PayloadJson
        };
        db.FinanceJournalSources.Add(row);
        var actor = "inbox." + FinanceJournalSourceInboxV1.ConsumerName;
        audit.Enqueue(new AuditEventDraft(row.CompanyId, "accounting.finance-journal-source.received.v1", "Accounting.FinanceJournalSource",
            row.Id.ToString("D"), "Receive", null, 1, AuditActorTypes.System, actor, actor, [], "{}",
            JsonSerializer.Serialize(View(row, null), Json), "Finans kaynağı alındı; muhasebe fişi ayrıca hazırlanır ve onaylanır.",
            "{}", AuditAiInvolvement.None, null, SourceChannels.System, null, null, envelope.CorrelationId,
            envelope.CausationId ?? envelope.EventId.ToString("D"), envelope.EventId.ToString("D")));
        try
        {
            Fence(source.CompanyId);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            db.ChangeTracker.Clear();
            auditOwner?.DiscardPending();
            throw;
        }
    }

    public async Task<IReadOnlyList<FinanceJournalSourceViewV1>> ListAsync(CancellationToken cancellationToken)
    {
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        Fence(companyId);
        var rows = await db.FinanceJournalSources.AsNoTracking().OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        Fence(companyId);
        var answer = new List<FinanceJournalSourceViewV1>(rows.Count);
        foreach (var row in rows)
        {
            if (row.SourceKind is NormalJournalSourceKindsV1.RecordedCashExchange or NormalJournalSourceKindsV1.ApprovedCashExchangeBookValue)
            {
                var exchangeSource = row.SourceKind == NormalJournalSourceKindsV1.ApprovedCashExchangeBookValue && exchanges is not null
                    ? await exchanges.ResolveAsync(companyId, row.SourceId, cancellationToken) : null;
                Fence(companyId);
                var exchangePreparation = NormalJournalDraftSourcePreparationV1.TryCreate(companyId, row.SourceId, exchangeSource);
                answer.Add(View(row, exchangePreparation?.PreparationVersion));
                continue;
            }
            var kind = await kinds.ResolveKindAsync(companyId, row.SourceId, cancellationToken);
            Fence(companyId);
            var rule = kind == row.SourceKind ? await bindings.ResolveAsync(companyId, row.SourceId, row.SourceKind,
                row.SourceVersion, row.OwnerProfileId, row.OwnerProfileVersion, row.CurrencyId, cancellationToken) : null;
            Fence(companyId);
            var preparation = rule is null ? null : NormalJournalDraftSourcePreparationV1.TryCreate(companyId, row.SourceId,
                new(rule, row.CurrencyId, row.SourceVersion, row.OwnerProfileId, row.OwnerProfileVersion, row.AmountText));
            answer.Add(View(row, preparation?.PreparationVersion));
        }
        return answer;
    }

    private void Fence(Guid companyId)
    {
        if (!company.IsResolved || companyId == Guid.Empty || company.CompanyId != companyId)
            throw new InvalidOperationException("Finans muhasebe kaynağının şirket bağlamı değişti.");
    }
    private static FinanceJournalSourceViewV1 View(FinanceJournalSourceRow row, string? preparation) => new(row.Id, row.CompanyId,
        row.SourceId, row.SourceKind, row.SourceVersion, row.CurrencyId, row.AmountText, row.OwnerProfileId, row.OwnerProfileVersion,
        row.OccurredOn, row.RecordedAtUtc, row.ReceivedAtUtc, "Received", preparation is null ? "BindingUnavailable" : "BindingAvailable", preparation,
        FinanceJournalSourceReaderV1.ReadStoredExchange(row));
}
