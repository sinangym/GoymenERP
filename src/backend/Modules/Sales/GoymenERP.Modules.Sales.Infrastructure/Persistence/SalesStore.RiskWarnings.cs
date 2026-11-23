using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// F-018 / plan S1 (FNS-D27(3)): the order's credit limit warnings — Sales' own append-only table, written only inside the submit /
/// approve save that produced them, with their own audit event on the order (no amount, ASSUMPTION-SAT-RISK-1). A replayed key
/// answers before anything is written, so a retry never records a second warning.
/// </summary>
public sealed partial class SalesStore
{
    private const string RiskWarningAuditReason = "Sipariş kredi limiti uyarısı kaydedildi";

    public async Task<IReadOnlyList<SalesOrderRiskWarningRecord>?> ListDraftRiskWarningsAsync(Guid draftId,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (draftId == Guid.Empty || !await dbContext.Drafts.AsNoTracking().AnyAsync(row => row.Id == draftId, cancellationToken))
        {
            return null;
        }

        var rows = await dbContext.DraftRiskWarnings.AsNoTracking().Where(row => row.DraftId == draftId)
            .OrderByDescending(row => row.DraftVersion).Take(SalesDraftRiskWarningsApiV1.MaximumListed + 1)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new SalesOrderRiskWarningRecord(row.Revision, row.DraftVersion, row.Command,
            (SalesOrderRiskWarningOutcome)row.Outcome, (SalesOrderRiskWarningCategory)row.Category, row.RecordedBy, row.RecordedAtUtc,
            row.CheckBusinessDay, row.RiskSourceContract))];
    }

    private static void RequireRiskWarning(string commandName, SalesOrderRiskAssessment warning)
    {
        if (commandName is not (SalesDraftCommands.SubmitV1 or SalesDraftCommands.ApproveV1))
        {
            throw new ArgumentException("Kredi limiti uyarısı yalnız onaya gönderme ve onayla kaydedilir.", nameof(warning));
        }

        var consistent = warning.Outcome switch
        {
            SalesOrderRiskWarningOutcome.Over => warning.Category == SalesOrderRiskWarningCategory.OverLimit,
            SalesOrderRiskWarningOutcome.Unavailable => warning.Category is SalesOrderRiskWarningCategory.CreditLimitNotSet
                or SalesOrderRiskWarningCategory.SourceUnavailable,
            _ => false,
        };
        if (!consistent || string.IsNullOrWhiteSpace(warning.RiskSourceContract) || warning.CheckBusinessDay == default)
        {
            throw new ArgumentException("Kredi limiti uyarısı tutarlı bir sonuç, kategori, iş günü ve kaynak sözleşmesi ister.", nameof(warning));
        }
    }

    private void AppendRiskWarning(SalesDraftWriteEnvelope envelope, Guid companyId, string commandName, SalesOrderDraft draft,
        SalesOrderRiskAssessment warning, DateTimeOffset now)
    {
        dbContext.DraftRiskWarnings.Add(new DraftRiskWarningRow
        {
            CompanyId = companyId,
            DraftId = draft.Id,
            DraftVersion = draft.Version,
            Revision = draft.Revision,
            Command = commandName,
            Outcome = (int)warning.Outcome,
            Category = (int)warning.Category,
            RecordedBy = envelope.ActorId.Trim(),
            RecordedAtUtc = now,
            CheckBusinessDay = warning.CheckBusinessDay,
            RiskSourceContract = warning.RiskSourceContract,
            CorrelationId = envelope.CorrelationId.Trim(),
        });

        // The order's own audit trail: outcome and category only — never an amount (ASSUMPTION-SAT-RISK-1).
        var after = new
        {
            command = commandName,
            revision = draft.Revision,
            version = draft.Version,
            outcome = warning.Outcome.ToString(),
            category = warning.Category.ToString(),
            checkBusinessDay = warning.CheckBusinessDay,
            riskSourceContract = warning.RiskSourceContract,
        };
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            SalesOrderRiskWarnings.RecordedEventType,
            DraftEntityType,
            draft.Id.ToString("D"),
            SalesOrderRiskWarnings.RecordedOperation,
            EntityVersionBefore: null,
            EntityVersionAfter: draft.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            "{}",
            JsonSerializer.Serialize(after, Json),
            RiskWarningAuditReason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey));
    }
}
