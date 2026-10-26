using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed class ManufacturingToleranceProfileLifecycleRow
{
    public Guid CompanyId { get; set; }
    public Guid ProfileId { get; set; }
    public string Process { get; set; } = string.Empty;
    public long ProfileVersion { get; set; }
    public decimal MaximumFirePercent { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveThrough { get; set; }
    public long StateVersion { get; set; }
    public ManufacturingToleranceProfileStatusV1 Status { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string PreparationReason { get; set; } = string.Empty;
    public string PreparationCorrelationId { get; set; } = string.Empty;
    public string? PreparationCausationId { get; set; }
    public string PreparationSourceChannel { get; set; } = string.Empty;
    public string? PreparationClientIpAddress { get; set; }
    public string? PreparationUserAgent { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? ApprovalReason { get; set; }
    public string? ApprovalCorrelationId { get; set; }
    public string? ApprovalCausationId { get; set; }
    public string? ApprovalSourceChannel { get; set; }
    public string? ApprovalClientIpAddress { get; set; }
    public string? ApprovalUserAgent { get; set; }
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? PublicationReason { get; set; }
    public string? PublicationCorrelationId { get; set; }
    public string? PublicationCausationId { get; set; }
    public string? PublicationSourceChannel { get; set; }
    public string? PublicationClientIpAddress { get; set; }
    public string? PublicationUserAgent { get; set; }
}
