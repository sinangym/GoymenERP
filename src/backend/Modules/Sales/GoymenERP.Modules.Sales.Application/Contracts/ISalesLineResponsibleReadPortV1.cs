namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>KLN-D110/SAT-D27: actual owner of an existing order line, from Sales' current recorded header.</summary>
public interface ISalesLineResponsibleReadPortV1
{
    /// <summary>Company is the server-resolved context. Never substitutes the actor or a role for an unassigned owner.</summary>
    Task<SalesLineResponsibleAnswerV1> ReadAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default);
}

public enum SalesLineResponsibleOutcomeV1 { Found = 1, NotFound = 2, Unassigned = 3, Unavailable = 4 }

public sealed record SalesLineResponsibleAnswerV1(SalesLineResponsibleOutcomeV1 Outcome, Guid CompanyId,
    Guid OrderId, Guid LineId, Guid? ResponsibleUserId, long? ObservedOrderVersion);
