using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Pure R47 transition policy. Persistence, authority, period, owner reads and idempotency remain
/// service responsibilities; this policy makes the closed human decision boundary deterministic.
/// </summary>
public static class ManualBankPaymentTransitionPolicy
{
    public static IReadOnlyList<ValidationIssue> ValidateApproval(
        ManualBankPaymentView payment,
        string expectedVersion,
        string actorId)
    {
        ArgumentNullException.ThrowIfNull(payment);
        var issues = Common(payment, expectedVersion, actorId);
        if (payment.Stage != ManualBankPaymentStage.Draft)
        {
            issues.Add(new("stage", "finance_manual_payment_not_draft"));
        }
        if (string.Equals(payment.PreparedBy, actorId, StringComparison.Ordinal))
        {
            issues.Add(new("actorId", "finance_manual_payment_preparer_cannot_approve"));
        }
        return issues;
    }

    public static IReadOnlyList<ValidationIssue> ValidateRecord(
        ManualBankPaymentView payment,
        string expectedVersion,
        string actorId,
        ManualBankResultConfirmation result)
    {
        ArgumentNullException.ThrowIfNull(payment);
        var issues = Common(payment, expectedVersion, actorId);
        if (payment.Stage != ManualBankPaymentStage.Approved || string.IsNullOrWhiteSpace(payment.ApprovedBy))
        {
            issues.Add(new("stage", "finance_manual_payment_not_approved"));
        }
        if (result != ManualBankResultConfirmation.Occurred)
        {
            issues.Add(new("result", "finance_manual_payment_result_not_confirmed"));
        }
        return issues;
    }

    public static ManualBankPaymentView Approve(
        ManualBankPaymentView payment,
        string actorId,
        DateTimeOffset approvedAtUtc)
    {
        var issues = ValidateApproval(payment, payment.Version, actorId);
        if (issues.Count != 0)
        {
            throw new InvalidOperationException(issues[0].Code);
        }
        return payment with
        {
            Version = Next(payment.Version),
            Stage = ManualBankPaymentStage.Approved,
            ApprovedBy = actorId,
            ApprovedAtUtc = approvedAtUtc,
        };
    }

    public static ManualBankPaymentView Record(
        ManualBankPaymentView payment,
        string actorId,
        DateTimeOffset recordedAtUtc,
        Guid moneyEventId)
    {
        var issues = ValidateRecord(payment, payment.Version, actorId, ManualBankResultConfirmation.Occurred);
        if (issues.Count != 0 || moneyEventId == Guid.Empty)
        {
            throw new InvalidOperationException(issues.Count == 0
                ? "finance_manual_payment_money_event_required"
                : issues[0].Code);
        }
        return payment with
        {
            Version = Next(payment.Version),
            Stage = ManualBankPaymentStage.Recorded,
            RecordedBy = actorId,
            RecordedAtUtc = recordedAtUtc,
            MoneyEventId = moneyEventId,
        };
    }

    private static List<ValidationIssue> Common(
        ManualBankPaymentView payment,
        string expectedVersion,
        string actorId)
    {
        var issues = new List<ValidationIssue>();
        if (payment.Id == Guid.Empty || payment.CompanyId == Guid.Empty)
        {
            issues.Add(new("paymentId", "identity_required"));
        }
        if (string.IsNullOrWhiteSpace(actorId) || !string.Equals(actorId, actorId.Trim(), StringComparison.Ordinal))
        {
            issues.Add(new("actorId", "actor_invalid"));
        }
        if (!string.Equals(payment.Version, expectedVersion, StringComparison.Ordinal))
        {
            issues.Add(new("expectedVersion", "version_conflict"));
        }
        return issues;
    }

    private static string Next(string version)
    {
        if (!long.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException("finance_manual_payment_version_invalid");
        }
        return checked(parsed + 1).ToString(CultureInfo.InvariantCulture);
    }
}
