using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<PurchaseSalesOrderObligationStatusV1>))]
public enum PurchaseSalesOrderObligationStatusV1 { Unavailable = 0, Open = 1, Complete = 2 }

/// <summary>Quantity/price-free Purchasing owner observation for this Sales order only. Complete includes no obligations;
/// Unavailable never establishes completion. This observation is not a durable token or a lock.</summary>
public sealed record PurchaseSalesOrderObligationReadV1(
    Guid CompanyId, Guid SalesOrderId, PurchaseSalesOrderObligationStatusV1 Status);

/// <summary>Reads Sales-sourced requests as well as their purchase commitments. The caller supplies its server-resolved
/// company and authoritative Sales order identity; the required callback freshly fences the consumer's own authority.
/// Draft/submitted needs and unsourced approved shares stay open. Rejected/withdrawn requests are terminal. Approved and
/// cancelled PO shares retain their original request usage (ALS-D68); only this order's share must finish, with technical
/// slices folded once by Purchasing's existing composer. Missing/invalid owner history fails closed.</summary>
public interface IPurchaseSalesOrderObligationReadPortV1
{
    Task<PurchaseSalesOrderObligationReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
