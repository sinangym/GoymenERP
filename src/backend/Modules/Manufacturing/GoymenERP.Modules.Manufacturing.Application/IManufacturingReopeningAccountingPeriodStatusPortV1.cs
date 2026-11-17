using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>URT-D36: the ordinary caller-authorized advisory read outside a reopening transaction, and an
/// Accounting-owned read enlisted in its Serializable transaction for the final fence. A generic separate-connection
/// period read cannot satisfy the final fence because its locks would expire before the reopening commits.</summary>
public interface IManufacturingReopeningAccountingPeriodStatusPortV1 : IAccountingPeriodStatusPortV1 { }
