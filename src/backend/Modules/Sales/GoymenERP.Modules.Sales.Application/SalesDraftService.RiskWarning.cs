using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// F-018 / plan S1 (FNS-D27(3)): a credit limit overrun on an order is a WARNING only. The order is saved; the warning is
/// appended to the order's own record and audit trail in the same save, and no separate approval is asked.
/// <list type="bullet">
/// <item>ASSUMPTION-SAT-RISK-2 (reversible): the warning is decided and recorded at submit and at approve (a revision's approval
/// included) — the same evaluation as the read-only risk check, on the order as it stands before the transition, so this order's
/// part is added to the gross risk. A draft save only shows the check on screen; there is no acknowledgement step.</item>
/// <item>ASSUMPTION-SAT-RISK-1 (reversible): the record carries the order, its revision and version, the actor, the UTC time and
/// business day, the outcome, the category and the risk read contract — never an amount. Retention follows the general audit
/// policy.</item>
/// <item>ASSUMPTION-SAT-RISK-4 (reversible): an outcome that cannot be decided is recorded with its own category
/// (<see cref="SalesOrderRiskWarningCategory.CreditLimitNotSet"/> / <see cref="SalesOrderRiskWarningCategory.SourceUnavailable"/>).</item>
/// <item>The owner reads are fenced on the command's own permission (the risk exception's shape, SAT-D162): a lost authority stays
/// a 401/403 and nothing is written; any other failure of the evaluation becomes a <c>SourceUnavailable</c> warning and never
/// blocks the order.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftRiskWarningsResponse> GetDraftRiskWarningsAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        IReadOnlyList<SalesOrderRiskWarningRecord>? warnings;
        try
        {
            warnings = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.ListDraftRiskWarningsAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (warnings is null)
        {
            // Another company's draft is null under the tenant filter: 404, never a 403.
            throw new SalesDraftServiceException(404, "not_found");
        }

        // The store answers newest first and at most one row beyond the page, so "truncated" is exact.
        var listed = warnings.Take(SalesDraftRiskWarningsApiV1.MaximumListed).Select(RiskWarningView).ToArray();
        return new SalesOrderDraftRiskWarningsResponse(listed, warnings.Count > SalesDraftRiskWarningsApiV1.MaximumListed);
    }

    private static SalesOrderDraftRiskWarningV1 RiskWarningView(SalesOrderRiskWarningRecord warning) => new(
        warning.Revision,
        warning.DraftVersion.ToString(CultureInfo.InvariantCulture),
        warning.Command,
        warning.Outcome switch
        {
            SalesOrderRiskWarningOutcome.Over => SalesDraftRiskWarningOutcomesV1.Over,
            SalesOrderRiskWarningOutcome.Unavailable => SalesDraftRiskWarningOutcomesV1.Unavailable,
            _ => throw new InvalidOperationException("Bilinmeyen risk uyarısı sonucu."),
        },
        warning.Category switch
        {
            SalesOrderRiskWarningCategory.OverLimit => SalesDraftRiskWarningCategoriesV1.OverLimit,
            SalesOrderRiskWarningCategory.CreditLimitNotSet => SalesDraftRiskWarningCategoriesV1.CreditLimitNotSet,
            SalesOrderRiskWarningCategory.SourceUnavailable => SalesDraftRiskWarningCategoriesV1.SourceUnavailable,
            _ => throw new InvalidOperationException("Bilinmeyen risk uyarısı kategorisi."),
        },
        warning.RecordedBy,
        warning.RecordedAtUtc,
        warning.CheckBusinessDay,
        warning.RiskSourceContract);

    /// <summary>
    /// Only a submission or an approval the domain would let through is evaluated (the re-approval floor's shape): a stale version,
    /// a closed order or a transition the approval axis refuses ends in its own terminal refusal and leaves no warning.
    /// </summary>
    private static bool RiskWarningDue(string commandName, SalesOrderDraft current, long expectedVersion) =>
        current.State == SalesDraftState.Draft && current.Version == expectedVersion && commandName switch
        {
            SalesDraftCommands.SubmitV1 => current.ApprovalState is InternalApprovalState.Preparing or InternalApprovalState.Rejected,
            SalesDraftCommands.ApproveV1 => current.ApprovalState == InternalApprovalState.PendingApproval,
            _ => false,
        };

    private async Task<SalesOrderRiskAssessment?> OrderRiskWarningAsync(Scope scope, string? permission, SalesOrderDraft current,
        CancellationToken cancellationToken)
    {
        SalesOrderRiskAssessment? warning;
        try
        {
            var (party, _, _, _, _, _, _, evaluation) = await EvaluateRiskAsync(scope, permission, current, cancellationToken);
            if (evaluation.Outcome == SalesRiskOutcome.Within)
            {
                return null;
            }

            warning = SalesOrderRiskWarnings.Classify(evaluation, party.CreditLimit, ServerBusinessDay());
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException { Status: 401 or 403 }))
        {
            warning = SalesOrderRiskWarnings.SourceUnavailable(ServerBusinessDay());
        }

        return warning;
    }
}
