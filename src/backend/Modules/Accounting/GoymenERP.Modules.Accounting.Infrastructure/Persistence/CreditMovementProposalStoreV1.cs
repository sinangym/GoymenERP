using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>FNS-D97 / TK-30: source acceptance is durable, but no unspecified account, VAT, exchange rate or posting is invented.</summary>
public sealed class CreditMovementProposalStoreV1(AccountingDbContext db, ICompanyContext company,
    ISettingsFinanceCreditAccountMappingProviderV1 mappings, IAuditEventCollector audit, TimeProvider clock,
    IAuditTransactionWriterV1? auditOwner = null, ICreditMovementNormalJournalSourceProviderV1? journalSource = null) : ICreditMovementProposalStoreV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken)
    {
        if (db.ChangeTracker.HasChanges() || auditOwner is { HasPendingDrafts: true })
            throw new InvalidOperationException("Kredi kaynak kabulü başka bir bekleyen yazıyla birleştirilemez.");
        var source = CreditMovementProposalInboxConsumerV1.Read(envelope, company);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.PayloadJson)));
        var reversal = source.ReversesMovementId is not null;
        var prior = await db.CreditMovementProposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == envelope.EventId
            || (x.MovementId == source.MovementId && x.IsReversal == reversal), cancellationToken);
        Fence(source.CompanyId);
        if (prior is not null)
        {
            if (prior.Id != envelope.EventId || prior.SourceHash != hash)
                throw new OutboxPermanentFailureException("credit_movement_source_conflict", "Kredi hareketi aynı kimlikle farklı kaynak taşıyor.");
            return;
        }
        var purpose = source.Kind switch
        {
            "Drawdown" => FinanceCreditAccountPurposeV1.CreditDrawdown,
            "Repayment" => FinanceCreditAccountPurposeV1.CreditRepayment,
            "InterestAccrual" => FinanceCreditAccountPurposeV1.CreditInterestAccrual,
            _ => (FinanceCreditAccountPurposeV1?)null,
        };
        SettingsFinanceCreditAccountMappingV1? mapping = null;
        if (purpose is { } requested)
        {
            mapping = await mappings.GetCreditAccountMappingAsync(source.CompanyId, requested, cancellationToken);
            Fence(source.CompanyId);
            if (mapping.CompanyId != source.CompanyId || mapping.Purpose != purpose || !Enum.IsDefined(mapping.Status)
                || mapping.Status == FinanceCreditAccountMappingStatusV1.Mapped && (mapping.Version < 1 || string.IsNullOrWhiteSpace(mapping.LedgerAccountCode)))
                throw new InvalidOperationException("Kredi muhasebe eşleme sahibi geçerli yanıt vermedi.");
        }
        var reasons = new List<string> { "credit_mapping_details_pending_TK30" };
        if (mapping is null || mapping.Status == FinanceCreditAccountMappingStatusV1.MappingMissing) reasons.Add("credit_mapping_missing");
        if (source.Components.Any(x => x.FunctionalAmount is null)) reasons.Add("credit_functional_source_amount_missing");
        var row = new CreditMovementProposalRow
        {
            Id = envelope.EventId, CompanyId = source.CompanyId, MovementId = source.MovementId, IsReversal = reversal,
            SourceHash = hash, SourceJson = envelope.PayloadJson,
            MappingStatus = mapping?.Status == FinanceCreditAccountMappingStatusV1.Mapped ? "MappingDetailsPending" : "MappingMissing",
            MappingVersion = mapping?.Status == FinanceCreditAccountMappingStatusV1.Mapped ? mapping.Version : null,
            WaitingReasonsJson = JsonSerializer.Serialize(reasons, Json), ReceivedAtUtc = clock.GetUtcNow()
        };
        db.CreditMovementProposals.Add(row);
        var actor = "inbox." + CreditMovementProposalInboxV1.ConsumerName;
        audit.Enqueue(new AuditEventDraft(row.CompanyId, "accounting.credit-movement-proposal.received.v1", "Accounting.CreditMovementProposal",
            row.Id.ToString("D"), "Receive", null, 1, AuditActorTypes.System, actor, actor, [], "{}", JsonSerializer.Serialize(View(row), Json),
            "Kredi kaynağı bir kez alındı; onaylı belge bileşeni eşlemesi olmadan fiş oluşmaz.", "{}", AuditAiInvolvement.None, null,
            SourceChannels.System, null, null, envelope.CorrelationId, envelope.CausationId ?? envelope.EventId.ToString("D"), envelope.EventId.ToString("D")));
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
    public async Task<IReadOnlyList<CreditMovementProposalViewV1>> ListAsync(CancellationToken cancellationToken)
    {
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        Fence(companyId);
        var rows = await db.CreditMovementProposals.AsNoTracking().OrderByDescending(x => x.ReceivedAtUtc).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        Fence(companyId);
        var result = new List<CreditMovementProposalViewV1>();
        foreach (var row in rows)
        {
            var view = View(row);
            if (journalSource is not null)
            {
                var source = await journalSource.ResolveAsync(companyId, row.Id, cancellationToken);
                Fence(companyId);
                view = source is null ? view with { MappingStatus = "MappingMissing", MappingVersion = null,
                    WaitingReasons = [CreditMovementJournalComponentsV1.Purpose(view.Source.Kind) is null || row.IsReversal
                        ? "credit_opening_journal_source_unavailable" : "credit_approved_source_package_missing"] }
                    : view with { Status = "ReadyForPreparation", MappingStatus = "MappingReady", MappingVersion = null, WaitingReasons = [] };
            }
            result.Add(view);
        }
        return result;
    }
    private void Fence(Guid companyId)
    {
        if (!company.IsResolved || companyId == Guid.Empty || company.CompanyId != companyId)
            throw new InvalidOperationException("Kredi muhasebe kaynağının şirket bağlamı değişti.");
    }
    private static CreditMovementProposalViewV1 View(CreditMovementProposalRow row) => new(row.Id, row.CompanyId, "AwaitingMapping",
        JsonSerializer.Deserialize<CreditMovementSourceV1>(row.SourceJson, Json) ?? throw new InvalidOperationException("Kredi kaynağı okunamadı."),
        row.MappingStatus, row.MappingVersion, JsonSerializer.Deserialize<string[]>(row.WaitingReasonsJson, Json)
            ?? throw new InvalidOperationException("Kredi bekleme gerekçesi okunamadı."), row.ReceivedAtUtc);
}
