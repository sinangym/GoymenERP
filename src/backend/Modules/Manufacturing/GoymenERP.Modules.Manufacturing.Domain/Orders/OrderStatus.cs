namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

public enum OrderStatus
{
    Open = 1,
    Closed = 2,

    /// <summary>
    /// F-009 D5 (URT-D33): the order was cancelled with no net effect left; its plan was released for a minimal
    /// re-approval. A cancelled order takes no new fact and is never reopened.
    /// </summary>
    Cancelled = 3,
}
