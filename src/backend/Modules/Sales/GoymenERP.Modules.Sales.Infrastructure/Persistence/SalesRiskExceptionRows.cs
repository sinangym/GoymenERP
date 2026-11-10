using GoymenERP.Modules.Sales.Domain.RiskExceptions;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-07 (SAT-D153…D162): one single-shipment risk exception of an approved order (<c>sales.ShipmentRiskExceptions</c>). The
/// request facts never change; the decision columns move once, Requested → Approved | Rejected (the write guard and the checks).
/// <see cref="LoadingInstructionId"/> is an opaque Logistics reference that Sales never reads or writes (SAT-D209).
/// </summary>
internal sealed class ShipmentRiskExceptionRow : ISalesTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public int ExceptionNo { get; set; }
    public Guid LoadingInstructionId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int State { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; set; }

    /// <summary>The approver or rejecter — never the requester (CK_ShipmentRiskExceptions_MakerChecker, SAT-D153/D159).</summary>
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? RejectionReason { get; set; }

    /// <summary>SAT-D160: the approval's business date (Türkiye); the exception is valid on that date only.</summary>
    public DateOnly? ValidOn { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>API-07 (ADR-011 §2–§8): the risk exception commands' own attempt records (append-only).</summary>
internal sealed class ShipmentRiskExceptionAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;

    /// <summary>1 = a stored exception (<see cref="ExceptionId"/> set); 2 = a recorded terminal refusal (<see cref="RefusalJson"/> set).</summary>
    public int Outcome { get; set; }
    public Guid OrderId { get; set; }
    public Guid? ExceptionId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal static class SalesRiskExceptionRows
{
    public static ShipmentRiskExceptionRow ToRow(SalesShipmentRiskException item, string correlationId) => new()
    {
        Id = item.Id,
        CompanyId = item.CompanyId,
        OrderId = item.OrderId,
        ExceptionNo = item.ExceptionNo,
        LoadingInstructionId = item.LoadingInstructionId,
        Reason = item.Reason,
        State = (int)item.State,
        RequestedBy = item.RequestedBy,
        RequestedAtUtc = item.RequestedAtUtc,
        DecidedBy = item.Decision?.DecidedBy,
        DecidedAtUtc = item.Decision?.DecidedAtUtc,
        RejectionReason = item.Decision?.RejectionReason,
        ValidOn = item.ValidOn,
        Version = item.Version,
        UpdatedAtUtc = item.UpdatedAtUtc,
        CorrelationId = correlationId.Trim(),
    };

    /// <summary>Rebuilds the domain exception through its own rules; a corrupt row is refused, never repaired.</summary>
    public static SalesShipmentRiskException Restore(ShipmentRiskExceptionRow row)
    {
        var item = SalesShipmentRiskException.Request(row.Id, row.CompanyId, row.OrderId, row.ExceptionNo, row.LoadingInstructionId,
            row.Reason, row.RequestedBy, row.RequestedAtUtc);
        if (!Enum.IsDefined((SalesShipmentRiskExceptionState)row.State))
        {
            throw new InvalidOperationException("Kayıtlı risk istisnası durumu tanınmıyor.");
        }

        var decision = row.DecidedBy is null && row.DecidedAtUtc is null && row.RejectionReason is null
            ? null
            : new SalesShipmentRiskExceptionDecision(
                row.DecidedBy ?? throw new InvalidOperationException("Kayıtlı risk istisnası kararı karar vereni taşımıyor."),
                row.DecidedAtUtc ?? throw new InvalidOperationException("Kayıtlı risk istisnası kararı zamanı taşımıyor."),
                row.RejectionReason);
        item.Restore((SalesShipmentRiskExceptionState)row.State, decision, row.ValidOn, row.Version, row.UpdatedAtUtc);
        return item;
    }

    /// <summary>Writes the one decision onto the tracked row: the decision columns, the version and the time only.</summary>
    public static void ApplyDecision(ShipmentRiskExceptionRow row, SalesShipmentRiskException item)
    {
        row.State = (int)item.State;
        row.DecidedBy = item.Decision?.DecidedBy;
        row.DecidedAtUtc = item.Decision?.DecidedAtUtc;
        row.RejectionReason = item.Decision?.RejectionReason;
        row.ValidOn = item.ValidOn;
        row.Version = item.Version;
        row.UpdatedAtUtc = item.UpdatedAtUtc;
    }
}
