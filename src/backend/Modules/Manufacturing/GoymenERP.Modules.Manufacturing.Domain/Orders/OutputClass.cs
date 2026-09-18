namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// Plan v2 §7.3: good product, second quality, usable remnant, scrap and process loss are
/// separate, and none of them is a place to put an unexplained difference. Work in progress is
/// not an output; it is recorded as its own position on the order.
/// </summary>
public enum OutputClass
{
    GoodProduct = 1,
    SecondQuality = 2,
    UsableRemnant = 3,
    Scrap = 4,
    ProcessLoss = 5,
}
