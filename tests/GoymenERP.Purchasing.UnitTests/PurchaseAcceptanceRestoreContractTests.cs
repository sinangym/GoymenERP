using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseAcceptanceRestoreContractTests
{
    private static readonly Guid Company = Guid.Parse("81000000-0000-0000-0000-000000000001");
    private static readonly Guid Order = Guid.Parse("82000000-0000-0000-0000-000000000001");
    private static readonly Guid Line = Guid.Parse("83000000-0000-0000-0000-000000000001");
    private static readonly Guid Product = Guid.Parse("84000000-0000-0000-0000-000000000001");
    private static readonly Guid Unit = Guid.Parse("85000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset At = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Restore_uses_durable_order_version_not_reverse_occurrence_time()
    {
        var first = Acceptance(Guid.Parse("86000000-0000-0000-0000-000000000001"), 4, 1m, At.AddMinutes(20));
        var second = Acceptance(Guid.Parse("86000000-0000-0000-0000-000000000002"), 5, 2m, At.AddMinutes(10));

        var restored = PurchasingStore.RestoreOrder(Header(5, At.AddMinutes(10)), [OrderLine(3m)], [second, first]);

        Assert.Equal(5, restored.Version);
        Assert.Equal([first.AcceptanceId, second.AcceptanceId],
            restored.Acceptances.Select(acceptance => acceptance.AcceptanceId).ToArray());
        var line = Assert.Single(restored.Lines);
        Assert.Equal(3m, line.AcceptedQuantity.Value);
        Assert.Equal(7m, line.RemainingQuantity.Value);
    }

    [Theory]
    [InlineData(4, 6)]
    [InlineData(4, 4)]
    [InlineData(3, 4)]
    public void Restore_refuses_gap_duplicate_or_corrupt_acceptance_versions(long firstVersion, long secondVersion)
    {
        var acceptances = new[]
        {
            Acceptance(Guid.Parse("86000000-0000-0000-0000-000000000011"), firstVersion, 1m, At.AddMinutes(20)),
            Acceptance(Guid.Parse("86000000-0000-0000-0000-000000000012"), secondVersion, 2m, At.AddMinutes(10)),
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PurchasingStore.RestoreOrder(Header(secondVersion, At.AddMinutes(10)), [OrderLine(3m)], acceptances));

        Assert.Equal("Kayıtlı kabul payı sipariş sürümünde ardışık değil.", exception.Message);
    }

    [Fact]
    public void Restore_refuses_a_terminal_order_version_that_does_not_equal_the_last_acceptance_version()
    {
        var acceptances = new[]
        {
            Acceptance(Guid.Parse("86000000-0000-0000-0000-000000000021"), 4, 1m, At.AddMinutes(20)),
            Acceptance(Guid.Parse("86000000-0000-0000-0000-000000000022"), 5, 2m, At.AddMinutes(10)),
        };

        Assert.Throws<InvalidOperationException>(() =>
            PurchasingStore.RestoreOrder(Header(6, At.AddMinutes(10)), [OrderLine(3m)], acceptances));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("order")]
    public void Restore_refuses_acceptance_history_bound_to_another_company_or_order(string changedIdentity)
    {
        var acceptance = Acceptance(Guid.Parse("86000000-0000-0000-0000-000000000031"), 4, 1m, At.AddMinutes(10));
        if (changedIdentity == "company") acceptance.CompanyId = Guid.NewGuid();
        if (changedIdentity == "order") acceptance.OrderId = Guid.NewGuid();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PurchasingStore.RestoreOrder(Header(4, At.AddMinutes(10)), [OrderLine(1m)], [acceptance]));

        Assert.Equal("Kayıtlı kabul payı sipariş sürümünde ardışık değil.", exception.Message);
    }

    private static PurchaseOrderRow Header(long version, DateTimeOffset updatedAtUtc) => new()
    {
        Id = Order,
        CompanyId = Company,
        SupplierPartyId = Guid.Parse("87000000-0000-0000-0000-000000000001"),
        CompanyAccountId = Guid.Parse("88000000-0000-0000-0000-000000000001"),
        Status = (int)PurchaseOrderStatus.Approved,
        Version = version,
        EffectiveOn = new DateOnly(2026, 9, 24),
        CreatedAtUtc = At,
        UpdatedAtUtc = updatedAtUtc,
        CreatedBy = "purchasing-restore-test",
        CorrelationId = "purchasing-restore-correlation",
    };

    private static PurchaseOrderLineRow OrderLine(decimal accepted) => new()
    {
        CompanyId = Company,
        OrderId = Order,
        LineNo = 1,
        LineId = Line,
        Kind = (int)PurchaseOrderLineKind.General,
        ProductId = Product,
        ProductVersion = "product-v1",
        UnitId = Unit,
        UnitIsKilogram = true,
        Ordered = 10m,
        Technical = 0m,
        Accepted = accepted,
    };

    private static PurchaseAcceptanceRow Acceptance(
        Guid acceptanceId,
        long orderVersionAfter,
        decimal quantity,
        DateTimeOffset recordedAtUtc) => new()
    {
        AcceptanceId = acceptanceId,
        CompanyId = Company,
        OrderId = Order,
        LineId = Line,
        Quantity = quantity,
        OrderVersionAfter = orderVersionAfter,
        RecordedAtUtc = recordedAtUtc,
        IdempotencyKeyHash = new string('A', 64),
        RequestHash = new string('B', 64),
        ActorId = "purchasing-restore-test",
        CorrelationId = "purchasing-restore-correlation",
        ResultJson = "{}",
    };
}
